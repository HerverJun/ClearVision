using System.Linq.Expressions;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Desktop.Endpoints;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ClearVision.Product.Desktop.Tests;

public sealed class UserEndpointsTests
{
    [Fact]
    public async Task UserEndpoints_ShouldRejectNonAdminUsers()
    {
        await using var host = await UserEndpointsTestHost.CreateAsync(role: UserRole.Engineer.ToString());

        using var response = await host.Client.GetAsync("/api/users");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UserEndpoints_ShouldSupportAdminCrudAndPasswordReset()
    {
        await using var host = await UserEndpointsTestHost.CreateAsync();

        using var createResponse = await host.Client.PostAsJsonAsync("/api/users", new CreateUserRequest
        {
            Username = "operator01",
            Password = "Password123",
            DisplayName = "Operator One",
            Role = UserRole.Operator
        });

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await ReadJsonAsync(createResponse);
        var userId = GetProperty(created, "id").GetString();
        userId.Should().NotBeNullOrWhiteSpace();
        GetProperty(created, "username").GetString().Should().Be("operator01");
        GetProperty(created, "displayName").GetString().Should().Be("Operator One");

        using var listResponse = await host.Client.GetAsync("/api/users");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var users = await ReadJsonAsync(listResponse);
        users.GetArrayLength().Should().Be(2);

        using var getResponse = await host.Client.GetAsync($"/api/users/{userId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetched = await ReadJsonAsync(getResponse);
        GetProperty(fetched, "username").GetString().Should().Be("operator01");

        using var updateResponse = await host.Client.PutAsJsonAsync($"/api/users/{userId}", new UpdateUserRequest
        {
            DisplayName = "Line Engineer",
            Role = UserRole.Engineer,
            IsActive = false
        });

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await ReadJsonAsync(updateResponse);
        GetProperty(updated, "displayName").GetString().Should().Be("Line Engineer");
        GetProperty(updated, "role").GetInt32().Should().Be((int)UserRole.Engineer);
        GetProperty(updated, "isActive").GetBoolean().Should().BeFalse();

        using var resetResponse = await host.Client.PostAsJsonAsync($"/api/users/{userId}/reset-password", new ResetPasswordRequest
        {
            NewPassword = "ResetPwd123"
        });

        resetResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var reset = await ReadJsonAsync(resetResponse);
        GetProperty(reset, "message").GetString().Should().Be("密码重置成功");
        var user = host.Repository.Users.Single(candidate => candidate.Username == "operator01");
        user.PasswordHash.Should().Be("hashed:ResetPwd123");

        using var deleteResponse = await host.Client.DeleteAsync($"/api/users/{userId}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        host.Repository.Users.Should().ContainSingle(user => user.Role == UserRole.Admin);

        using var missingResponse = await host.Client.GetAsync($"/api/users/{userId}");
        missingResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateUser_ShouldRejectPasswordsShorterThanConfiguredMinimum()
    {
        await using var host = await UserEndpointsTestHost.CreateAsync(passwordMinLength: 10);

        using var response = await host.Client.PostAsJsonAsync("/api/users", new CreateUserRequest
        {
            Username = "engineer01",
            Password = "Pwd12345",
            DisplayName = "Engineer One",
            Role = UserRole.Engineer
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var payload = await ReadJsonAsync(response);
        GetProperty(payload, "error").GetString().Should().Be("初始密码长度不能少于 10 位");
        host.Repository.Users.Should().ContainSingle(user => user.Role == UserRole.Admin);
    }

    [Fact]
    public async Task AdminEndpoints_ShouldFailClosedForSelfDeleteAndSelfDemotion()
    {
        await using var host = await UserEndpointsTestHost.CreateAsync();
        var adminId = host.AdminId;
        adminId.Should().NotBeNull();

        using var updateResponse = await host.Client.PutAsJsonAsync($"/api/users/{adminId}", new UpdateUserRequest
        {
            DisplayName = "Still Admin",
            Role = UserRole.Engineer,
            IsActive = false
        });
        updateResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var deleteResponse = await host.Client.DeleteAsync($"/api/users/{adminId}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        host.Repository.Users.Should().ContainSingle(user => user.Id == adminId && user.Role == UserRole.Admin && user.IsActive);
    }

    [Fact]
    public async Task AdminEndpoints_ShouldPreserveAnActiveAdminWhenRemovingAnotherAdmin()
    {
        await using var host = await UserEndpointsTestHost.CreateAsync();
        var secondAdmin = User.Create("second-admin", "hashed:Password123", "Second Admin", UserRole.Admin);
        await host.Repository.AddAsync(secondAdmin);

        using var response = await host.Client.PutAsJsonAsync($"/api/users/{secondAdmin.Id}", new UpdateUserRequest
        {
            DisplayName = "Disabled Admin",
            Role = UserRole.Operator,
            IsActive = false
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.Repository.Users.Count(user => user.Role == UserRole.Admin && user.IsActive).Should().Be(1);
        host.Repository.Users.Should().Contain(user => user.Id == host.AdminId && user.Role == UserRole.Admin && user.IsActive);
    }

    [Fact]
    public async Task ResetPassword_ShouldReturnNotFound_WhenUserDoesNotExist()
    {
        await using var host = await UserEndpointsTestHost.CreateAsync();
        var missingUserId = Guid.NewGuid();

        using var response = await host.Client.PostAsJsonAsync($"/api/users/{missingUserId}/reset-password", new ResetPasswordRequest
        {
            NewPassword = "Password123"
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var payload = await ReadJsonAsync(response);
        GetProperty(payload, "error").GetString().Should().Be("用户不存在");
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static JsonElement GetProperty(JsonElement element, string propertyName)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.NameEquals(propertyName) || property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        throw new KeyNotFoundException(propertyName);
    }

    private sealed class UserEndpointsTestHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private UserEndpointsTestHost(WebApplication app, InMemoryUserRepository repository, Guid? adminId)
        {
            _app = app;
            Repository = repository;
            AdminId = adminId;
            Client = app.GetTestClient();
        }

        public HttpClient Client { get; }

        public InMemoryUserRepository Repository { get; }

        public Guid? AdminId { get; }

        public static async Task<UserEndpointsTestHost> CreateAsync(string? role = "Admin", int passwordMinLength = 8)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });

            builder.WebHost.UseTestServer();

            var repository = new InMemoryUserRepository();
            User? admin = null;
            if (string.Equals(role, UserRole.Admin.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                admin = User.Create("admin", "hashed:AdminPassword", "Administrator", UserRole.Admin);
                await repository.AddAsync(admin);
            }
            var configService = Substitute.For<IConfigurationService>();
            configService.GetCurrent().Returns(new AppConfig
            {
                Security = new SecurityConfig
                {
                    PasswordMinLength = passwordMinLength
                }
            });

            builder.Services.AddSingleton<IUserRepository>(repository);
            builder.Services.AddSingleton(repository);
            builder.Services.AddSingleton<IPasswordHasher>(new FixedPasswordHasher());
            builder.Services.AddSingleton<UserManagementService>();
            builder.Services.AddSingleton(configService);
            builder.Services
                .AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
            builder.Services.AddAuthorization();

            var app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();
            app.Use(async (context, next) =>
            {
                if (role is not null)
                {
                    context.Items["CurrentUser"] = new UserSession
                    {
                        UserId = admin?.Id.ToString() ?? role.ToLowerInvariant(),
                        Username = role.ToLowerInvariant(),
                        Role = role
                    };
                }

                await next();
            });

            app.MapUserEndpoints();
            await app.StartAsync();
            return new UserEndpointsTestHost(app, repository, admin?.Id);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "Test";

        public TestAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "test-user"),
                new Claim(ClaimTypes.Name, "test-user")
            ], SchemeName);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    private sealed class FixedPasswordHasher : IPasswordHasher
    {
        public string HashPassword(string password) => $"hashed:{password}";

        public bool VerifyPassword(string password, string hash) => hash == HashPassword(password);
    }

    private sealed class InMemoryUserRepository : IUserRepository
    {
        private readonly Dictionary<Guid, User> _users = new();

        public IReadOnlyCollection<User> Users => _users.Values.ToArray();

        public Task<User?> GetByIdAsync(Guid id)
        {
            _users.TryGetValue(id, out var user);
            return Task.FromResult(user);
        }

        public Task<IEnumerable<User>> GetAllAsync()
        {
            return Task.FromResult<IEnumerable<User>>(_users.Values.ToArray());
        }

        public Task<IEnumerable<User>> FindAsync(Expression<Func<User, bool>> predicate)
        {
            var compiled = predicate.Compile();
            return Task.FromResult<IEnumerable<User>>(_users.Values.Where(compiled).ToArray());
        }

        public Task<User> AddAsync(User entity)
        {
            _users[entity.Id] = entity;
            return Task.FromResult(entity);
        }

        public Task UpdateAsync(User entity)
        {
            _users[entity.Id] = entity;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(User entity)
        {
            _users.Remove(entity.Id);
            return Task.CompletedTask;
        }

        public Task DeleteByIdAsync(Guid id)
        {
            _users.Remove(id);
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(Guid id)
        {
            return Task.FromResult(_users.ContainsKey(id));
        }

        public Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
        {
            var user = _users.Values.FirstOrDefault(candidate =>
                string.Equals(candidate.Username, username, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(user);
        }

        public Task<IEnumerable<User>> GetAllActiveUsersAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IEnumerable<User>>(_users.Values.Where(user => user.IsActive).ToArray());
        }

        public Task<bool> IsUsernameExistsAsync(string username, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_users.Values.Any(user =>
                string.Equals(user.Username, username, StringComparison.OrdinalIgnoreCase)));
        }

        public Task<bool> HasAnyUsersAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_users.Count > 0);
        }
    }
}
