# TemplateMatching resource trace

- Trace date: 2026-07-10
- Source commit: `f740128deef47b4795d5cfa37fc8c30185a31cb0`
- Scope: trace `TemplateId` / `TemplatePath` from selection and draft metadata to the formal `TemplateMatching.Template` runtime input.
- Mutation policy: evidence only; no field, port, workflow, or runtime behavior was changed by this trace.

## Conclusion

No production chain was found that converts a `TemplateId` or `TemplatePath` flow parameter into the formal `TemplateMatching.Template` image input.

The repository currently contains two different identifier domains that use similar names:

1. Flow-template selection identifiers select a workflow skeleton. They are consumed by `ScenarioMatcher` and `TemplateStrategyResolver` and do not represent a template image.
2. Legacy TemplateMatching resource metadata (`TemplateId` / `TemplatePath`) is accepted by Agent draft, precheck, and metadata-only preview governance. It can satisfy those metadata checks, but no inspected implementation loads an image or creates a connection to the `Template` input from either field.

The only proven execution path is an actual image arriving at the required `Template` input port. Therefore this round keeps the formal port and both legacy metadata fields unchanged, keeps TemplateMatching outside the new canonical parameter provider, and labels its parity cases as legacy metadata semantics.

## Evidence chain

| Stage | Evidence | Finding |
| --- | --- | --- |
| Formal operator contract | `ClearVision.Product/src/ClearVision.Product.Infrastructure/Operators/TemplateMatchOperator.cs:26-28` | `Image` and `Template` are required image input ports. There is no formal `TemplateId` or `TemplatePath` parameter attribute. |
| Runtime execution | `TemplateMatchOperator.cs:83-110` | Execution calls `TryGetInputImage(inputs, "Template", ...)` and fails when that image is absent. It never reads `TemplateId` or `TemplatePath`. |
| Flow-template recommendation | `ClearVision.Product/src/ClearVision.Product.Infrastructure/AI/ScenarioMatcher.cs:290-299` | `TemplateId` is emitted from a matched `FlowTemplate.Id`; this identifier selects a workflow template, not a template image. |
| Flow-template strategy | `ClearVision.Product/src/ClearVision.Product.Infrastructure/AI/Agent/Build/TemplateStrategyResolver.cs:34-72` | Selected or matched `TemplateId` is passed to `get_flow_template_skeleton`. The result is a metadata-only workflow skeleton. |
| Agent parameter mapping | `ClearVision.Product/src/ClearVision.Product.Infrastructure/AI/Agent/Build/ParameterMappingService.cs:209-215,266-275,337-344` | Generic template-like formal parameters may receive a pending placeholder, but TemplateMatching's formal metadata has no such parameter. Its explicit mappings only cover `Threshold` and `MaxMatches`. |
| Agent connection building | `ClearVision.Product/src/ClearVision.Product.Infrastructure/AI/Agent/Build/WorkflowDraftBuilder.cs:371-378` | TemplateMatching receives the normal `Image` connection. No connection is built for the required `Template` port. |
| Current build/readiness behavior | `ClearVision.Product/tests/ClearVision.Product.Tests/AI/VisionAgentBuildOrchestrator/VisionAgentBuildOrchestratorTests.cs:1505-1513` | The generated template-matching pipeline reports `op_match.Template` as the missing template resource. |
| Current generate-flow behavior | `ClearVision.Product/tests/ClearVision.Product.Tests/AI/VisionAgentGenerateFlow/VisionAgentGenerateFlowTests.cs:558-562` | The draft contains TemplateMatching, reports a template artifact, and explicitly does not emit `TemplatePath`. |
| Legacy Agent acceptance | `ClearVision.Product/src/ClearVision.Product.Infrastructure/AI/Tools/VisionAgentParameterRuleCenter.cs` | Agent validation treats any of `Template`, `TemplateId`, or `TemplatePath` as sufficient metadata. This is a readiness rule, not a runtime conversion. |
| Draft normalization | `ClearVision.Product/src/ClearVision.Product.Infrastructure/AI/Tools/VisionAgentFlowDraftNormalizer.cs:89-109,121-133` | Arbitrary parameter dictionaries are preserved, and a direct `TemplatePath` property is copied into parameters. No image is loaded and no input connection is created. |
| Metadata-only preview | `ClearVision.Product/src/ClearVision.Product.Infrastructure/AI/Tools/RuntimePreviewGovernanceServices.cs:3100,4247,4815-4824` | Preview governance requires/allowlists `TemplateId`, denies external `TemplatePath`, and is explicitly metadata-only. These checks do not feed the runtime image input. |
| Shared legacy parity | `quality/evals/specs/vision_agent_parameter_rule_parity_cases.json` | TemplateMatching cases remain separate from provider-backed operators and document metadata acceptance versus the actual missing `Template` resource. |

## Searches performed

The trace covered production and test references under `ClearVision.Product/src`, `ClearVision.Product/tests`, and `quality/evals/specs` for:

- `TemplateMatchOperator`, `TemplateMatching`, and the formal `Template` port;
- `TemplateId` and `TemplatePath` creation, normalization, mapping, validation, precheck, and preview handling;
- `ScenarioMatcher`, `TemplateStrategyResolver`, `ParameterMappingService`, and `WorkflowDraftBuilder`;
- tests asserting current build, readiness, and preview behavior.

No inspected code path resolved a template catalog identifier or filesystem path to an `ImageWrapper`, wrote such an image into flow inputs, or created a connection targeting the `Template` port.

## Boundary for later work

Any future attempt to make `TemplateId` executable needs an explicit, reviewed resource resolver that produces the `Template` image input, plus save/load, preview, dry-run, run, and deployment tests. `TemplatePath` must not be enabled merely because legacy drafts preserve it; current preview governance deliberately rejects external paths.
