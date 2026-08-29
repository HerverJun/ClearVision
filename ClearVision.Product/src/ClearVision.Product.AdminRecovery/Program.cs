using ClearVision.Product.Infrastructure.Data;
using ClearVision.Product.Infrastructure.Security;
using ClearVision.Product.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace ClearVision.Product.AdminRecovery;

internal static class Program
{
    private const string EnableEnvironmentVariable = "CLEARVISION_ENABLE_LOCAL_ADMIN_RECOVERY";
    private const string RequiredConfirmation = "RECOVER_LOCAL_ADMIN";
    private const int MinimumRecoveryPasswordLength = 12;

    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (!string.Equals(
                    Environment.GetEnvironmentVariable(EnableEnvironmentVariable),
                    "1",
                    StringComparison.Ordinal))
            {
                return Fail($"Recovery is disabled. Set {EnableEnvironmentVariable}=1 for this process only.");
            }

            var options = ParseOptions(args);
            if (!string.Equals(options.Confirmation, RequiredConfirmation, StringComparison.Ordinal))
            {
                return Fail($"Pass --confirm {RequiredConfirmation} to acknowledge the break-glass operation.");
            }

            var databasePath = ResolveLocalDatabasePath(options.DatabasePath);
            var password = ReadSecret("New Admin password: ");
            var confirmation = ReadSecret("Confirm password: ");
            if (!string.Equals(password, confirmation, StringComparison.Ordinal))
            {
                return Fail("Passwords do not match.");
            }

            if (password.Length < MinimumRecoveryPasswordLength)
            {
                return Fail($"Recovery password must contain at least {MinimumRecoveryPasswordLength} characters.");
            }

            var dbOptions = new DbContextOptionsBuilder<VisionDbContext>()
                .UseSqlite($"Data Source={databasePath};Pooling=False")
                .Options;
            await using var dbContext = new VisionDbContext(dbOptions);
            await dbContext.Database.MigrateAsync();

            var passwordHasher = new PasswordHasher();
            var recovery = new LocalAdminRecoveryService(dbContext);
            var result = await recovery.RecoverAsync(
                options.Username,
                passwordHasher.HashPassword(password));

            Console.WriteLine(
                result.WasCreated
                    ? $"Created local recovery Admin '{result.Username}' ({result.UserId})."
                    : $"Restored local Admin '{result.Username}' ({result.UserId}) and reset its password.");
            Console.WriteLine("The installation latch remains completed. Clear the enable environment variable now.");
            return 0;
        }
        catch (Exception ex)
        {
            return Fail($"Recovery failed: {ex.Message}");
        }
    }

    private static RecoveryOptions ParseOptions(IReadOnlyList<string> args)
    {
        string? databasePath = null;
        string? username = null;
        string? confirmation = null;

        for (var index = 0; index < args.Count; index++)
        {
            var option = args[index];
            if (index + 1 >= args.Count)
            {
                throw new ArgumentException($"Missing value for {option}.");
            }

            var value = args[++index];
            switch (option)
            {
                case "--database":
                    databasePath = value;
                    break;
                case "--username":
                    username = value;
                    break;
                case "--confirm":
                    confirmation = value;
                    break;
                default:
                    throw new ArgumentException($"Unknown option: {option}");
            }
        }

        if (string.IsNullOrWhiteSpace(databasePath) || string.IsNullOrWhiteSpace(username))
        {
            throw new ArgumentException(
                "Usage: --database <absolute-local-vision.db> --username <user> " +
                $"--confirm {RequiredConfirmation}");
        }

        return new RecoveryOptions(databasePath, username.Trim(), confirmation);
    }

    private static string ResolveLocalDatabasePath(string path)
    {
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("The database path must be absolute.");
        }

        var fullPath = Path.GetFullPath(path);
        if (new Uri(fullPath).IsUnc)
        {
            throw new ArgumentException("UNC/network database paths are not allowed.");
        }

        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root) || new DriveInfo(root).DriveType == DriveType.Network)
        {
            throw new ArgumentException("The recovery database must be on a local drive.");
        }

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The recovery database does not exist.", fullPath);
        }

        return fullPath;
    }

    private static string ReadSecret(string prompt)
    {
        Console.Write(prompt);
        if (Console.IsInputRedirected)
        {
            return Console.ReadLine() ?? string.Empty;
        }

        var characters = new List<char>();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return new string(characters.ToArray());
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (characters.Count > 0)
                {
                    characters.RemoveAt(characters.Count - 1);
                }

                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                characters.Add(key.KeyChar);
            }
        }
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 2;
    }

    private sealed record RecoveryOptions(string DatabasePath, string Username, string? Confirmation);
}
