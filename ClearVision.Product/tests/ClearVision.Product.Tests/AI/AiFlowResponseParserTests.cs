using System.Text.Json;
using ClearVision.Product.Infrastructure.AI;
using FluentAssertions;

namespace ClearVision.Product.Tests.AI;

[TestClassification(TestDomain.Ai, TestPurpose.Regression, TestLane.Nightly, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Medium, TestFlakyPolicy.Blocking, "vision-agent")]
public class AiFlowResponseParserTests
{
    [Fact(DisplayName = "AiFlowResponseParser should parse top-level escaped workflow JSON strings")]
    public void Parse_TopLevelEscapedJsonString_ShouldParseWorkflow()
    {
        var workflowJson = """
            {
              "explanation": "ok",
              "operators": [
                {
                  "tempId": "op_1",
                  "operatorType": "ImageAcquisition",
                  "displayName": "Camera",
                  "parameters": { "SourceType": "Camera" }
                }
              ],
              "connections": []
            }
            """;
        var rawResponse = JsonSerializer.Serialize(workflowJson);

        var result = new AiFlowResponseParser().Parse(rawResponse);

        result.Success.Should().BeTrue();
        result.CandidateCount.Should().Be(1);
        result.Flow!.Operators.Should().ContainSingle();
        result.Flow.Operators[0].TempId.Should().Be("op_1");
        result.Flow.Operators[0].OperatorType.Should().Be("ImageAcquisition");
    }

    [Fact(DisplayName = "AiFlowResponseParser should normalize mapped operators and connections")]
    public void Parse_MappedOperatorsAndConnections_ShouldNormalizeWorkflow()
    {
        const string rawResponse = """
            {
              "workflow": {
                "explanation": "mapped shape",
                "operators": {
                  "op_1": {
                    "operatorType": "ImageAcquisition",
                    "displayName": "Camera",
                    "parameters": { "SourceType": "Camera" }
                  },
                  "op_2": {
                    "operatorType": "ResultOutput",
                    "displayName": "Output",
                    "parameters": { "Format": "JSON", "SaveToFile": false }
                  }
                },
                "connections": {
                  "c1": {
                    "source": "op_1.Image",
                    "target": "op_2.Result"
                  }
                },
                "parameters_needing_review": [
                  { "operator_id": "op_2", "parameters": ["Format"] }
                ]
              }
            }
            """;

        var result = new AiFlowResponseParser().Parse(rawResponse);

        result.Success.Should().BeTrue();
        result.Flow!.Operators.Should().HaveCount(2);
        result.Flow.Operators[0].TempId.Should().Be("op_1");
        result.Flow.Operators[1].Parameters["SaveToFile"].Should().Be("false");
        result.Flow.Connections.Should().ContainSingle(connection =>
            connection.SourceTempId == "op_1" &&
            connection.SourcePortName == "Image" &&
            connection.TargetTempId == "op_2" &&
            connection.TargetPortName == "Result");
        result.Flow.ParametersNeedingReview["op_2"].Should().ContainSingle("Format");
    }
}
