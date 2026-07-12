import settingsApi from '../settingsApi.js';
import { showToast } from '../../../shared/components/uiComponents.js';

export function installRuntimePreviewPilotConsole(SettingsView) {
    const baseRenderRuntimePreviewPilotPanel = SettingsView.prototype.renderRuntimePreviewPilotPanel;

    Object.assign(SettingsView.prototype, {
        renderRuntimePreviewPilotConsoleTab() {
            return `
                <div class="settings-section-title" data-runtime-preview-pilot-console-page="true">
                    <h2>RuntimePreview Pre-release Review Desk</h2>
                    <p>Metadata-only scenario, manifest dry-run, package readiness, and governance evidence for developer review.</p>
                </div>
                ${this.renderScopeNotice('runtime-preview-pilot')}
                ${this.renderRuntimePreviewPilotPanel()}
            `;
        }
        ,
        renderRuntimePreviewPilotPanel() {
            const html = baseRenderRuntimePreviewPilotPanel.call(this).replace('RuntimePreview Pilot Console v1.1', 'RuntimePreview Pilot Console v1.4');
            const extras = this.renderRuntimePreviewPilotConsoleV12Panels();
            return html.replace(
                '</div>\n                </details>',
                `${extras}\n                    </div>\n                </details>`);
        }
        ,
        renderRuntimePreviewPilotConsoleV12Panels() {
            const corpus = this.runtimePreviewScenarioCorpus || null;
            const redactedCorpus = this.runtimePreviewRedactedFlowCorpus || null;
            const packageReadiness = this.runtimePreviewPilotPackageReadinessReport || null;
            const manifestDryRun = this.runtimePackageManifestDryRunReport || null;
            const stationProfiles = this.runtimePreviewStationProfiles || null;
            const operatorRegistry = this.runtimePreviewOperatorContractRegistry || null;
            const operatorCoverage = this.runtimePreviewOperatorContractCoverageReport || null;
            const preReleaseReview = this.runtimePreviewPreReleaseReviewReport || null;
            const stationCompatibility = this.runtimePreviewStationCompatibilityReport || null;
            const operatorValidation = this.runtimePreviewOperatorContractValidationReport || null;
            const governanceIndex = this.runtimePreviewGovernanceIndex || null;
            const governanceExport = this.runtimePreviewGovernanceExport || null;
            const governanceLookup = this.runtimePreviewGovernanceLookup || null;
            const explanation = this.runtimePreviewAgentExplanationBenchmark || null;
            const corpusCases = Array.isArray(corpus?.cases) ? corpus.cases : [];
            const redactedCases = Array.isArray(redactedCorpus?.cases) ? redactedCorpus.cases : [];
            const stationProfileRows = Array.isArray(stationProfiles?.profiles) ? stationProfiles.profiles : [];
            const corpusOptions = corpusCases
                .map(item => `<option value="${this.sanitizeRuntimePreviewPilotValue(item.caseId)}">${this.sanitizeRuntimePreviewPilotValue(`${item.caseId} ${item.scenario}`)}</option>`)
                .join('');
            const redactedOptions = redactedCases
                .map(item => `<option value="${this.sanitizeRuntimePreviewPilotValue(item.caseId)}">${this.sanitizeRuntimePreviewPilotValue(`${item.caseId} ${item.workflowKind}`)}</option>`)
                .join('');
            const stationProfileOptions = stationProfileRows
                .map(item => `<option value="${this.sanitizeRuntimePreviewPilotValue(item.stationProfileId)}">${this.sanitizeRuntimePreviewPilotValue(`${item.stationProfileId} ${item.stationType}`)}</option>`)
                .join('');
            const corpusRows = corpusCases.length
                ? corpusCases.slice(0, 18).map(item => `
                    <tr>
                        <td>${this.sanitizeRuntimePreviewPilotValue(item.caseId)}</td>
                        <td>${this.sanitizeRuntimePreviewPilotValue(item.scenario)}</td>
                        <td>${this.sanitizeRuntimePreviewPilotValue(item.expectedStatus)}</td>
                        <td>${this.sanitizeRuntimePreviewPilotValue(item.expectedRisk)}</td>
                        <td>${this.sanitizeRuntimePreviewPilotValue(item.businessExplanation)}</td>
                    </tr>
                `).join('')
                : '<tr><td colspan="5" style="color:#64748b;">Scenario corpus has not been loaded.</td></tr>';
            const redactedRows = redactedCases.length
                ? redactedCases.slice(0, 24).map(item => `
                    <tr>
                        <td>${this.sanitizeRuntimePreviewPilotValue(item.caseId)}</td>
                        <td>${this.sanitizeRuntimePreviewPilotValue(item.stationProfileId || item.stationType)}</td>
                        <td>${this.sanitizeRuntimePreviewPilotValue(item.workflowKind)}</td>
                        <td>${this.sanitizeRuntimePreviewPilotValue(item.expectedManifestRisk)}</td>
                        <td>${this.sanitizeRuntimePreviewPilotValue(item.expectedStationCompatibility)}</td>
                        <td>${this.sanitizeRuntimePreviewPilotValue(item.expectedReleaseReviewDecision)}</td>
                        <td>${this.sanitizeRuntimePreviewPilotValue(item.expectedEngineerAction)}</td>
                    </tr>
                `).join('')
                : '<tr><td colspan="7" style="color:#64748b;">Redacted flow corpus has not been loaded.</td></tr>';
            const releaseDecisionState = preReleaseReview?.releaseReviewAllowed === true
                ? 'releaseAllowed'
                : preReleaseReview?.requiresEngineerApproval === true
                    ? 'approvalRequired'
                    : preReleaseReview
                        ? 'blocked'
                        : 'notRun';
            const releaseDecisionSummaryHtml = preReleaseReview
                ? `<div data-rp-release-decision-state="${this.sanitizeRuntimePreviewPilotValue(releaseDecisionState)}" style="display:grid; grid-template-columns:repeat(3,minmax(0,1fr)); gap:8px; margin-bottom:8px;">
                    <div data-rp-release-allowed-state="${preReleaseReview.releaseReviewAllowed === true}" style="padding:8px; border:1px solid #cbd5e1; border-radius:8px; background:${preReleaseReview.releaseReviewAllowed === true ? '#dcfce7' : '#f8fafc'};">releaseAllowed: ${this.sanitizeRuntimePreviewPilotValue(preReleaseReview.releaseReviewAllowed === true)}</div>
                    <div data-rp-approval-required-state="${preReleaseReview.requiresEngineerApproval === true}" style="padding:8px; border:1px solid #cbd5e1; border-radius:8px; background:${preReleaseReview.requiresEngineerApproval === true ? '#fef3c7' : '#f8fafc'};">approvalRequired: ${this.sanitizeRuntimePreviewPilotValue(preReleaseReview.requiresEngineerApproval === true)}</div>
                    <div data-rp-blocked-state="${preReleaseReview.releaseReviewAllowed !== true && preReleaseReview.requiresEngineerApproval !== true}" style="padding:8px; border:1px solid #cbd5e1; border-radius:8px; background:${preReleaseReview.releaseReviewAllowed !== true && preReleaseReview.requiresEngineerApproval !== true ? '#fee2e2' : '#f8fafc'};">blocked: ${this.sanitizeRuntimePreviewPilotValue(preReleaseReview.releaseReviewAllowed !== true && preReleaseReview.requiresEngineerApproval !== true)}</div>
                </div>`
                : '<div data-rp-release-decision-state="notRun" style="font-size:12px; color:#64748b; margin-bottom:8px;">Release decision has not been generated.</div>';
            const preReleaseHtml = preReleaseReview
                ? `<pre data-rp-pre-release-review-report="true" style="white-space:pre-wrap; font-size:11px; max-height:210px; overflow:auto;">${this.sanitizeRuntimePreviewPilotValue(JSON.stringify({
                    reviewId: preReleaseReview.reviewId,
                    caseId: preReleaseReview.caseId,
                    sessionId: preReleaseReview.sessionId,
                    workflowDraftHash: preReleaseReview.workflowDraftHash,
                    manifestId: preReleaseReview.manifestId,
                    stationProfileId: preReleaseReview.stationProfileId,
                    operatorContractVersion: preReleaseReview.operatorContractVersion,
                    readinessStatus: preReleaseReview.readinessStatus,
                    packageReviewAllowed: preReleaseReview.packageReviewAllowed,
                    stationCompatible: preReleaseReview.stationCompatible,
                    operatorContractsSatisfied: preReleaseReview.operatorContractsSatisfied,
                    releaseReviewAllowed: preReleaseReview.releaseReviewAllowed,
                    requiresEngineerApproval: preReleaseReview.requiresEngineerApproval,
                    goNoGoDecision: preReleaseReview.goNoGoDecision,
                    riskLevel: preReleaseReview.riskLevel,
                    blockedReasons: preReleaseReview.blockedReasons || [],
                    engineerActions: preReleaseReview.engineerActions || [],
                    firstFixRecommendation: preReleaseReview.firstFixRecommendation,
                    workflowDraftAllowed: preReleaseReview.workflowDraftAllowed,
                    decisionMatrix: preReleaseReview.decisionMatrix,
                    metadataOnly: preReleaseReview.metadataOnly,
                    packageCreated: preReleaseReview.packageCreated,
                    deploymentExecuted: preReleaseReview.deploymentExecuted,
                    realResourcesTouched: preReleaseReview.realResourcesTouched
                }, null, 2))}</pre>`
                : '<div data-rp-pre-release-review-report="true" style="font-size:12px; color:#64748b;">No pre-release review report generated.</div>';
            const packageHtml = packageReadiness
                ? `<pre data-rp-package-readiness-report="true" style="white-space:pre-wrap; font-size:11px; max-height:170px; overflow:auto;">${this.sanitizeRuntimePreviewPilotValue(JSON.stringify({
                    reportId: packageReadiness.reportId,
                    sessionId: packageReadiness.sessionId,
                    readyForPackage: packageReadiness.readyForPackage,
                    packageReviewAllowed: packageReadiness.packageReviewAllowed,
                    packageBlocked: packageReadiness.packageBlocked,
                    manifestDryRunReportId: packageReadiness.manifestDryRunReportId,
                    packageCreated: packageReadiness.packageCreated,
                    deploymentExecuted: packageReadiness.deploymentExecuted,
                    workflowDraftAllowed: packageReadiness.workflowDraftAllowed,
                    riskSummary: packageReadiness.riskSummary,
                    packageRiskLevel: packageReadiness.packageRiskLevel,
                    packageReviewExplanation: packageReadiness.packageReviewExplanation,
                    blockingIssues: packageReadiness.blockingIssues || [],
                    missingResources: packageReadiness.missingResources || [],
                    pendingActions: packageReadiness.pendingActions || [],
                    operatorTrace: packageReadiness.operatorTrace || [],
                    resourceTrace: packageReadiness.resourceTrace || [],
                    dependencyTrace: packageReadiness.dependencyTrace || []
                }, null, 2))}</pre>`
                : '<div data-rp-package-readiness-report="true" style="font-size:12px; color:#64748b;">No package readiness report generated.</div>';
            const manifestHtml = manifestDryRun
                ? `<pre data-rp-manifest-dry-run-report="true" style="white-space:pre-wrap; font-size:11px; max-height:190px; overflow:auto;">${this.sanitizeRuntimePreviewPilotValue(JSON.stringify({
                    manifestId: manifestDryRun.manifestId,
                    manifestHash: manifestDryRun.manifestHash,
                    workflowDraftHash: manifestDryRun.workflowDraftHash,
                    operatorCount: manifestDryRun.operatorCount,
                    operatorTypes: manifestDryRun.operatorTypes || [],
                    resourceDependencies: manifestDryRun.resourceDependencies || [],
                    modelDependencies: manifestDryRun.modelDependencies || [],
                    templateDependencies: manifestDryRun.templateDependencies || [],
                    cameraBindings: manifestDryRun.cameraBindings || [],
                    outputChannels: manifestDryRun.outputChannels || [],
                    missingDependencies: manifestDryRun.missingDependencies || [],
                    blockedReasons: manifestDryRun.blockedReasons || [],
                    dependencyTrace: manifestDryRun.dependencyTrace || [],
                    riskLevel: manifestDryRun.riskLevel,
                    packageReviewAllowed: manifestDryRun.packageReviewAllowed,
                    manifestArtifactGenerated: manifestDryRun.manifestArtifactGenerated,
                    packageCreated: manifestDryRun.packageCreated,
                    deploymentExecuted: manifestDryRun.deploymentExecuted
                }, null, 2))}</pre>`
                : '<div data-rp-manifest-dry-run-report="true" style="font-size:12px; color:#64748b;">No manifest dry-run report generated.</div>';
            const stationHtml = stationCompatibility
                ? `<pre data-rp-station-compatibility-report="true" style="white-space:pre-wrap; font-size:11px; max-height:190px; overflow:auto;">${this.sanitizeRuntimePreviewPilotValue(JSON.stringify({
                    reportId: stationCompatibility.reportId,
                    manifestId: stationCompatibility.manifestId,
                    stationProfileId: stationCompatibility.stationProfileId,
                    stationCompatible: stationCompatibility.stationCompatible,
                    runtimeVersionCompatible: stationCompatibility.runtimeVersionCompatible,
                    operatorSupportCompatible: stationCompatibility.operatorSupportCompatible,
                    cameraSlotsCompatible: stationCompatibility.cameraSlotsCompatible,
                    outputChannelsCompatible: stationCompatibility.outputChannelsCompatible,
                    modelTemplateDependenciesCompatible: stationCompatibility.modelTemplateDependenciesCompatible,
                    operatorCountCompatible: stationCompatibility.operatorCountCompatible,
                    plcStationIntentCompatible: stationCompatibility.plcStationIntentCompatible,
                    manifestRiskCompatible: stationCompatibility.manifestRiskCompatible,
                    blockedReasons: stationCompatibility.blockedReasons || [],
                    riskLevel: stationCompatibility.riskLevel,
                    engineerActions: stationCompatibility.engineerActions || [],
                    networkPolicy: stationCompatibility.stationProfile?.networkPolicy || 'redacted',
                    plcWriteAllowed: stationCompatibility.stationProfile?.plcWriteAllowed === true
                }, null, 2))}</pre>`
                : '<div data-rp-station-compatibility-report="true" style="font-size:12px; color:#64748b;">No station compatibility dry-run generated.</div>';
            const operatorHtml = operatorValidation
                ? `<pre data-rp-operator-contract-validation-report="true" style="white-space:pre-wrap; font-size:11px; max-height:190px; overflow:auto;">${this.sanitizeRuntimePreviewPilotValue(JSON.stringify({
                    reportId: operatorValidation.reportId,
                    manifestId: operatorValidation.manifestId,
                    stationProfileId: operatorValidation.stationProfileId,
                    operatorContractVersion: operatorValidation.operatorContractVersion,
                    operatorContractsSatisfied: operatorValidation.operatorContractsSatisfied,
                    blockedReasons: operatorValidation.blockedReasons || [],
                    riskTags: operatorValidation.riskTags || [],
                    requiredEngineerApprovals: operatorValidation.requiredEngineerApprovals || [],
                    contractResults: operatorValidation.contractResults || []
                }, null, 2))}</pre>`
                : '<div data-rp-operator-contract-validation-report="true" style="font-size:12px; color:#64748b;">No operator contract validation generated.</div>';
            const stationProfilesHtml = stationProfiles
                ? `<pre data-rp-station-profiles="true" style="white-space:pre-wrap; font-size:11px; max-height:130px; overflow:auto;">${this.sanitizeRuntimePreviewPilotValue(JSON.stringify({
                    profileCount: stationProfiles.profileCount,
                    profiles: stationProfileRows.map(item => ({
                        stationProfileId: item.stationProfileId,
                        stationType: item.stationType,
                        runtimeVersion: item.runtimeVersion,
                        supportedOperatorTypes: item.supportedOperatorTypes || [],
                        supportedModelKinds: item.supportedModelKinds || [],
                        cameraBindingSlots: item.cameraBindingSlots || [],
                        outputChannelKinds: item.outputChannelKinds || [],
                        maxOperatorCount: item.maxOperatorCount,
                        plcWriteAllowed: item.plcWriteAllowed === true,
                        networkPolicy: item.networkPolicy || 'redacted'
                    }))
                }, null, 2))}</pre>`
                : '<div data-rp-station-profiles="true" style="font-size:12px; color:#64748b;">No station profiles loaded.</div>';
            const operatorRegistryHtml = operatorRegistry
                ? `<pre data-rp-operator-contract-registry="true" style="white-space:pre-wrap; font-size:11px; max-height:130px; overflow:auto;">${this.sanitizeRuntimePreviewPilotValue(JSON.stringify({
                    operatorContractVersion: operatorRegistry.operatorContractVersion,
                    contractCount: operatorRegistry.contractCount,
                    coveragePass: operatorCoverage?.coveragePass,
                    missingOperatorTypes: operatorCoverage?.missingOperatorTypes || [],
                    contracts: operatorRegistry.contracts || []
                }, null, 2))}</pre>`
                : '<div data-rp-operator-contract-registry="true" style="font-size:12px; color:#64748b;">No operator contract registry loaded.</div>';
            const operatorCoverageHtml = operatorCoverage
                ? `<pre data-rp-operator-contract-coverage="true" style="white-space:pre-wrap; font-size:11px; max-height:130px; overflow:auto;">${this.sanitizeRuntimePreviewPilotValue(JSON.stringify({
                    reportId: operatorCoverage.reportId,
                    operatorContractVersion: operatorCoverage.operatorContractVersion,
                    contractCount: operatorCoverage.contractCount,
                    coveragePass: operatorCoverage.coveragePass,
                    missingOperatorTypes: operatorCoverage.missingOperatorTypes || [],
                    coveredOperatorTypes: operatorCoverage.coveredOperatorTypes || []
                }, null, 2))}</pre>`
                : '<div data-rp-operator-contract-coverage="true" style="font-size:12px; color:#64748b;">No operator contract coverage report loaded.</div>';
            const governanceIndexHtml = governanceIndex
                ? `<pre data-rp-governance-index="true" style="white-space:pre-wrap; font-size:11px; max-height:130px; overflow:auto;">${this.sanitizeRuntimePreviewPilotValue(JSON.stringify(governanceIndex, null, 2))}</pre>`
                : '<div data-rp-governance-index="true" style="font-size:12px; color:#64748b;">No governance index loaded.</div>';
            const artifactSummaryHtml = governanceExport
                ? `<pre data-rp-artifact-report-summary="true" style="white-space:pre-wrap; font-size:11px; max-height:130px; overflow:auto;">${this.sanitizeRuntimePreviewPilotValue(JSON.stringify({
                    exportId: governanceExport.exportId,
                    streamCounts: governanceExport.indexSummary || {},
                    redactionPass: governanceExport.redactionPass,
                    metadataOnly: governanceExport.metadataOnly,
                    realResourcesTouched: governanceExport.realResourcesTouched
                }, null, 2))}</pre>`
                : '<div data-rp-artifact-report-summary="true" style="font-size:12px; color:#64748b;">No artifact report summary preview loaded.</div>';
            const governanceExportHtml = governanceExport
                ? `<pre data-rp-governance-export="true" style="white-space:pre-wrap; font-size:11px; max-height:130px; overflow:auto;">${this.sanitizeRuntimePreviewPilotValue(JSON.stringify({
                    exportId: governanceExport.exportId,
                    generatedAtUtc: governanceExport.generatedAtUtc,
                    indexSummary: governanceExport.indexSummary,
                    redactionPass: governanceExport.redactionPass,
                    metadataOnly: governanceExport.metadataOnly,
                    realResourcesTouched: governanceExport.realResourcesTouched
                }, null, 2))}</pre>`
                : '<div data-rp-governance-export="true" style="font-size:12px; color:#64748b;">No governance export loaded.</div>';
            const lookupHtml = governanceLookup
                ? `<pre data-rp-governance-lookup="true" style="white-space:pre-wrap; font-size:11px; max-height:130px; overflow:auto;">${this.sanitizeRuntimePreviewPilotValue(JSON.stringify(governanceLookup, null, 2))}</pre>`
                : '<div data-rp-governance-lookup="true" style="font-size:12px; color:#64748b;">No lookup result loaded.</div>';
            const explanationHtml = explanation
                ? `<pre data-rp-agent-explanation="true" style="white-space:pre-wrap; font-size:11px; max-height:170px; overflow:auto;">${this.sanitizeRuntimePreviewPilotValue(JSON.stringify({
                    caseCount: explanation.caseCount,
                    passedCaseCount: explanation.passedCaseCount,
                    accepted: explanation.accepted,
                    expectedFields: ['readyStateExplanation', 'missingResourceExplanation', 'packageRiskExplanation', 'operatorContractExplanation', 'stationCompatibilityExplanation', 'releaseDecisionExplanation', 'workflowDraftVsReleaseExplanation', 'nextEngineerAction'],
                    cases: explanation.cases || []
                }, null, 2))}</pre>`
                : '<div data-rp-agent-explanation="true" style="font-size:12px; color:#64748b;">Agent explanation benchmark has not been loaded.</div>';

            return `
                        <div style="margin-top:14px; border-top:1px solid var(--border-color); padding-top:12px;" data-rp-scenario-corpus-panel="true">
                            <div style="display:flex; align-items:center; justify-content:space-between; gap:8px;">
                                <h4 style="margin:0 0 8px;">Scenario Corpus</h4>
                                <div style="display:flex; gap:8px; align-items:center;">
                                    <select class="cv-input" id="cfg-rp-scenario-case-id" style="min-width:260px;">${corpusOptions}</select>
                                    <button class="cv-btn settings-btn-light" id="btn-runtime-preview-pilot-load-corpus">Load corpus</button>
                                    <button class="cv-btn settings-btn-light" id="btn-runtime-preview-pilot-run-selected-scenario">Run selected scenario</button>
                                </div>
                            </div>
                            <table class="settings-modern-table" data-rp-scenario-corpus="true">
                                <thead><tr><th>Case</th><th>Scenario</th><th>Status</th><th>Risk</th><th>Business explanation</th></tr></thead>
                                <tbody>${corpusRows}</tbody>
                            </table>
                        </div>
                        <div style="margin-top:14px; border-top:1px solid var(--border-color); padding-top:12px;" data-rp-redacted-flow-corpus-panel="true">
                            <div style="display:flex; align-items:center; justify-content:space-between; gap:8px;">
                                <h4 style="margin:0 0 8px;">Redacted Flow Corpus</h4>
                                <div style="display:flex; gap:8px; align-items:center;">
                                    <select class="cv-input" id="cfg-rp-redacted-flow-case-id" style="min-width:260px;">${redactedOptions}</select>
                                    <select class="cv-input" id="cfg-rp-station-profile-id" style="min-width:260px;">${stationProfileOptions}</select>
                                    <button class="cv-btn settings-btn-light" id="btn-runtime-preview-pilot-load-redacted-flow-corpus">Load redacted flows</button>
                                    <button class="cv-btn settings-btn-light" id="btn-runtime-preview-pilot-load-station-profiles">Load station profiles</button>
                                    <button class="cv-btn settings-btn-light" id="btn-runtime-preview-pilot-load-operator-contract-registry">Load contracts</button>
                                    <button class="cv-btn settings-btn-light" id="btn-runtime-preview-pilot-run-redacted-flow-chain">Run full review chain</button>
                                </div>
                            </div>
                            <table class="settings-modern-table" data-rp-redacted-flow-corpus="true">
                                <thead><tr><th>Case</th><th>Station profile</th><th>Workflow kind</th><th>Manifest risk</th><th>Station</th><th>Decision</th><th>Engineer action</th></tr></thead>
                                <tbody>${redactedRows}</tbody>
                            </table>
                            ${stationProfilesHtml}
                            ${operatorRegistryHtml}
                            ${operatorCoverageHtml}
                        </div>
                        <div style="margin-top:14px; border-top:1px solid var(--border-color); padding-top:12px;" data-rp-pre-release-review-panel="true">
                            <h4 style="margin:0 0 8px;">Release review decision</h4>
                            ${releaseDecisionSummaryHtml}
                            ${preReleaseHtml}
                        </div>
                        <div style="margin-top:14px; border-top:1px solid var(--border-color); padding-top:12px;" data-rp-package-readiness-panel="true">
                            <div style="display:flex; align-items:center; justify-content:space-between; gap:8px;">
                                <h4 style="margin:0 0 8px;">Package readiness bridge</h4>
                                <button class="cv-btn settings-btn-light" id="btn-runtime-preview-pilot-package-readiness">Package readiness report</button>
                            </div>
                            ${packageHtml}
                        </div>
                        <div style="margin-top:14px; border-top:1px solid var(--border-color); padding-top:12px;" data-rp-manifest-dry-run-panel="true">
                            <div style="display:flex; align-items:center; justify-content:space-between; gap:8px;">
                                <h4 style="margin:0 0 8px;">RuntimePackage manifest dry-run</h4>
                                <button class="cv-btn settings-btn-light" id="btn-runtime-preview-pilot-manifest-dry-run">Manifest dry-run</button>
                            </div>
                            ${manifestHtml}
                        </div>
                        <div style="margin-top:14px; border-top:1px solid var(--border-color); padding-top:12px;" data-rp-station-compatibility-panel="true">
                            <h4 style="margin:0 0 8px;">Station compatibility dry-run</h4>
                            ${stationHtml}
                        </div>
                        <div style="margin-top:14px; border-top:1px solid var(--border-color); padding-top:12px;" data-rp-operator-contract-validation-panel="true">
                            <h4 style="margin:0 0 8px;">Operator contract validation</h4>
                            ${operatorHtml}
                        </div>
                        <div style="margin-top:14px; border-top:1px solid var(--border-color); padding-top:12px;" data-rp-governance-panel="true">
                            <h4 style="margin:0 0 8px;">Governance index, lookup, and export</h4>
                            <div style="display:grid; grid-template-columns:repeat(7,minmax(0,1fr)); gap:8px;">
                                <input class="cv-input" id="cfg-rp-lookup-session-id" placeholder="sessionId">
                                <input class="cv-input" id="cfg-rp-lookup-report-id" placeholder="reportId">
                                <input class="cv-input" id="cfg-rp-lookup-case-id" placeholder="caseId">
                                <input class="cv-input" id="cfg-rp-lookup-manifest-id" placeholder="manifestId">
                                <input class="cv-input" id="cfg-rp-lookup-review-id" placeholder="reviewId">
                                <input class="cv-input" id="cfg-rp-lookup-station-profile-id" placeholder="stationProfileId">
                                <input class="cv-input" id="cfg-rp-lookup-operator-type" placeholder="operatorType">
                            </div>
                            <div style="display:flex; gap:8px; justify-content:flex-end; margin-top:8px;">
                                <button class="cv-btn settings-btn-light" id="btn-runtime-preview-pilot-governance-index">Load index</button>
                                <button class="cv-btn settings-btn-light" id="btn-runtime-preview-pilot-governance-lookup">Lookup</button>
                                <button class="cv-btn settings-btn-light" id="btn-runtime-preview-pilot-governance-export">Export manifest</button>
                                <button class="cv-btn settings-btn-light" id="btn-runtime-preview-pilot-agent-explanation">Agent explanation</button>
                            </div>
                            ${governanceIndexHtml}
                            ${lookupHtml}
                            ${governanceExportHtml}
                            ${artifactSummaryHtml}
                            ${explanationHtml}
                        </div>
            `;
        }
        ,
        bindRuntimePreviewPilotConsoleEvents() {
            const consoleTab = this.container?.querySelector('[data-section="runtime-preview-pilot"]');
            if (!consoleTab) return;

            consoleTab.addEventListener('click', async (e) => {
                const btn = e.target.closest('button');
                if (!btn) return;
                await this.handleRuntimePreviewPilotConsoleButton(consoleTab, btn);
            });
        }
        ,
        async handleRuntimePreviewPilotConsoleButton(root, btn) {
            try {
                if (btn.id === 'btn-runtime-preview-pilot-refresh') {
                    await this.loadRuntimePreviewPilotState();
                } else if (btn.id === 'btn-runtime-preview-pilot-save') {
                    const payload = this.readRuntimePreviewPilotConfigDraft(root);
                    this.runtimePreviewPilotConfigDiff = this.buildRuntimePreviewPilotConfigDiff(this.runtimePreviewPilotConfig || {}, payload);
                    const confirmed = typeof window?.confirm === 'function'
                        ? window.confirm('Save metadata-only RuntimePreview Pilot allowlist changes?')
                        : true;
                    if (!confirmed) return;
                    const result = await settingsApi.saveRuntimePreviewPilotConfig(payload);
                    this.runtimePreviewPilotConfig = this.normalizeRuntimePreviewPilotConfig(result);
                    await this.loadRuntimePreviewPilotState();
                    showToast('RuntimePreview Pilot config saved.', 'success');
                } else if (btn.id === 'btn-runtime-preview-pilot-apply-catalog-allowlist') {
                    this.applyRuntimePreviewPilotCatalogAllowlist(root);
                } else if (btn.id === 'btn-runtime-preview-pilot-readiness') {
                    const result = await settingsApi.checkRuntimePreviewPilotReadiness(this.buildRuntimePreviewPilotSessionPayload(root));
                    this.runtimePreviewPilotReadiness = result?.readiness || result;
                    this.runtimePreviewPilotCatalog = result?.catalog || this.runtimePreviewPilotCatalog;
                } else if (btn.id === 'btn-runtime-preview-pilot-create-session') {
                    const result = await settingsApi.createRuntimePreviewPilotSession(this.buildRuntimePreviewPilotSessionPayload(root));
                    this.runtimePreviewPilotSelectedSessionId = result?.session?.sessionId || null;
                    await this.loadRuntimePreviewPilotState();
                } else if (btn.id === 'btn-runtime-preview-pilot-simulate') {
                    const result = await settingsApi.simulateRuntimePreviewPilotSession(this.buildRuntimePreviewPilotSessionPayload(root));
                    this.runtimePreviewPilotSessionReport = result?.report || null;
                    this.runtimePreviewPilotSelectedSessionId = result?.session?.sessionId || result?.report?.sessionId || null;
                    await this.loadRuntimePreviewPilotState();
                } else if (btn.id === 'btn-runtime-preview-pilot-load-report') {
                    const sessionId = this.getRuntimePreviewPilotSelectedSessionId(root);
                    if (!sessionId) return;
                    const result = await settingsApi.loadRuntimePreviewPilotSessionReport(sessionId);
                    this.runtimePreviewPilotSessionReport = result?.report || null;
                    this.runtimePreviewPilotSelectedSessionId = sessionId;
                } else if (btn.id === 'btn-runtime-preview-pilot-replay-session') {
                    const sessionId = this.getRuntimePreviewPilotSelectedSessionId(root);
                    if (!sessionId) return;
                    const result = await settingsApi.replayRuntimePreviewPilotSession(sessionId);
                    this.runtimePreviewPilotReplay = result?.replay || null;
                    this.runtimePreviewPilotSelectedSessionId = sessionId;
                } else if (btn.id === 'btn-runtime-preview-pilot-export-report') {
                    const sessionId = this.getRuntimePreviewPilotSelectedSessionId(root);
                    if (!sessionId) return;
                    const result = await settingsApi.exportRuntimePreviewPilotSessionReport(sessionId);
                    this.runtimePreviewPilotExport = result?.export || null;
                    this.runtimePreviewPilotSelectedSessionId = sessionId;
                } else if (btn.id === 'btn-runtime-preview-pilot-deploy-readiness') {
                    const result = await settingsApi.generateRuntimePreviewDeployReadiness(this.buildRuntimePreviewPilotSessionPayload(root));
                    this.runtimePreviewPilotDeployReadinessReport = result?.deployReadinessReport || null;
                    this.runtimePreviewPilotSessionReport = result?.deployReadinessReport?.simulationReport || this.runtimePreviewPilotSessionReport;
                    this.runtimePreviewPilotSelectedSessionId = result?.deployReadinessReport?.sessionId || this.runtimePreviewPilotSelectedSessionId;
                    await this.loadRuntimePreviewPilotState();
                } else if (btn.id === 'btn-runtime-preview-pilot-package-readiness') {
                    const result = await settingsApi.generateRuntimePreviewPackageReadiness(this.buildRuntimePreviewPilotSessionPayload(root));
                    this.runtimePreviewPilotPackageReadinessReport = result?.packageReadinessReport || null;
                    this.runtimePackageManifestDryRunReport = result?.manifestDryRunReport || this.runtimePackageManifestDryRunReport;
                    this.runtimePreviewPilotSelectedSessionId = result?.packageReadinessReport?.sessionId || this.runtimePreviewPilotSelectedSessionId;
                    await this.loadRuntimePreviewPilotState();
                } else if (btn.id === 'btn-runtime-preview-pilot-manifest-dry-run') {
                    const result = await settingsApi.generateRuntimePackageManifestDryRun(this.buildRuntimePreviewPilotSessionPayload(root));
                    this.runtimePreviewPilotPackageReadinessReport = result?.packageReadinessReport || null;
                    this.runtimePackageManifestDryRunReport = result?.manifestDryRunReport || null;
                    this.runtimePreviewPilotSelectedSessionId = result?.packageReadinessReport?.sessionId || this.runtimePreviewPilotSelectedSessionId;
                    await this.loadRuntimePreviewPilotState();
                } else if (btn.id === 'btn-runtime-preview-pilot-scenario-evidence') {
                    const result = await settingsApi.loadRuntimePreviewScenarioEvidence();
                    this.runtimePreviewPilotScenarioEvidence = result?.evidence || result;
                } else if (btn.id === 'btn-runtime-preview-pilot-load-corpus') {
                    const result = await settingsApi.loadRuntimePreviewScenarioCorpus();
                    this.runtimePreviewScenarioCorpus = result?.corpus || result;
                } else if (btn.id === 'btn-runtime-preview-pilot-load-redacted-flow-corpus') {
                    const result = await settingsApi.loadRuntimePreviewRedactedFlowCorpus();
                    this.runtimePreviewRedactedFlowCorpus = result?.corpus || result;
                } else if (btn.id === 'btn-runtime-preview-pilot-load-station-profiles') {
                    const result = await settingsApi.loadRuntimePreviewStationProfiles();
                    this.runtimePreviewStationProfiles = result?.stationProfiles || result;
                } else if (btn.id === 'btn-runtime-preview-pilot-load-operator-contract-registry') {
                    const result = await settingsApi.loadRuntimePreviewOperatorContractRegistry();
                    this.runtimePreviewOperatorContractRegistry = result?.operatorContractRegistry || result;
                    this.runtimePreviewOperatorContractCoverageReport = result?.operatorContractCoverageReport || this.runtimePreviewOperatorContractCoverageReport;
                } else if (btn.id === 'btn-runtime-preview-pilot-run-selected-scenario') {
                    await this.ensureRuntimePreviewScenarioCorpusLoaded();
                    const caseId = root.querySelector('#cfg-rp-scenario-case-id')?.value || '';
                    const scenarioCase = this.findRuntimePreviewScenarioCase(caseId);
                    if (!scenarioCase) return;
                    const payload = {
                        config: this.readRuntimePreviewPilotConfigDraft(root),
                        toolName: 'runtime_preview_metadata',
                        runtimePreviewConsent: true,
                        arguments: { flow: scenarioCase.workflowDraft }
                    };
                    const result = await settingsApi.generateRuntimePreviewPackageReadiness(payload);
                    this.runtimePreviewPilotPackageReadinessReport = result?.packageReadinessReport || null;
                    this.runtimePackageManifestDryRunReport = result?.manifestDryRunReport || this.runtimePackageManifestDryRunReport;
                    this.runtimePreviewGovernanceLookup = { corpusCase: scenarioCase };
                } else if (btn.id === 'btn-runtime-preview-pilot-run-redacted-flow-chain') {
                    await this.ensureRuntimePreviewRedactedFlowCorpusLoaded();
                    await this.ensureRuntimePreviewStationProfilesLoaded();
                    const caseId = root.querySelector('#cfg-rp-redacted-flow-case-id')?.value || '';
                    const flowCase = this.findRuntimePreviewRedactedFlowCase(caseId);
                    if (!flowCase) return;
                    const stationProfileId = root.querySelector('#cfg-rp-station-profile-id')?.value || flowCase.stationProfileId || '';
                    const payload = {
                        config: this.readRuntimePreviewPilotConfigDraft(root),
                        toolName: 'runtime_preview_metadata',
                        runtimePreviewConsent: true,
                        arguments: { flow: flowCase.workflowDraft },
                        caseId: flowCase.caseId,
                        stationProfileId
                    };
                    const result = await settingsApi.generateRuntimePreviewPreReleaseReview(payload);
                    this.runtimePreviewPreReleaseReviewReport = result?.preReleaseReviewReport || null;
                    this.runtimePreviewPilotPackageReadinessReport = result?.packageReadinessReport || null;
                    this.runtimePackageManifestDryRunReport = result?.manifestDryRunReport || null;
                    this.runtimePreviewStationCompatibilityReport = result?.stationCompatibilityReport || null;
                    this.runtimePreviewOperatorContractValidationReport = result?.operatorContractValidationReport || null;
                    this.runtimePreviewGovernanceLookup = { redactedFlowCase: flowCase, preReleaseReviewReport: this.runtimePreviewPreReleaseReviewReport };
                } else if (btn.id === 'btn-runtime-preview-pilot-governance-index') {
                    const result = await settingsApi.loadRuntimePreviewGovernanceIndex();
                    this.runtimePreviewGovernanceIndex = result?.index || result;
                } else if (btn.id === 'btn-runtime-preview-pilot-governance-export') {
                    const result = await settingsApi.exportRuntimePreviewGovernance();
                    this.runtimePreviewGovernanceExport = result?.export || result;
                } else if (btn.id === 'btn-runtime-preview-pilot-governance-lookup') {
                    const result = await settingsApi.lookupRuntimePreviewGovernance({
                        sessionId: root.querySelector('#cfg-rp-lookup-session-id')?.value || '',
                        reportId: root.querySelector('#cfg-rp-lookup-report-id')?.value || '',
                        caseId: root.querySelector('#cfg-rp-lookup-case-id')?.value || '',
                        manifestId: root.querySelector('#cfg-rp-lookup-manifest-id')?.value || '',
                        reviewId: root.querySelector('#cfg-rp-lookup-review-id')?.value || '',
                        stationProfileId: root.querySelector('#cfg-rp-lookup-station-profile-id')?.value || '',
                        operatorType: root.querySelector('#cfg-rp-lookup-operator-type')?.value || ''
                    });
                    this.runtimePreviewGovernanceLookup = result?.lookup || result;
                } else if (btn.id === 'btn-runtime-preview-pilot-agent-explanation') {
                    const result = await settingsApi.loadRuntimePreviewAgentExplanationBenchmark();
                    this.runtimePreviewAgentExplanationBenchmark = result?.benchmark || result;
                } else if (btn.id === 'btn-runtime-preview-pilot-cleanup') {
                    const retentionDays = Number(root.querySelector('#cfg-rp-retention-days')?.value || 30);
                    const maxSessions = Number(root.querySelector('#cfg-rp-max-sessions')?.value || 200);
                    const result = await settingsApi.cleanupRuntimePreviewPilotRetention({ retentionDays, maxSessions });
                    this.runtimePreviewPilotRetentionCleanup = result?.cleanup || result;
                    await this.loadRuntimePreviewPilotState();
                } else if (btn.id === 'btn-runtime-preview-pilot-cancel-session') {
                    const sessionId = this.getRuntimePreviewPilotSelectedSessionId(root);
                    if (!sessionId) return;
                    await settingsApi.cancelRuntimePreviewPilotSession(sessionId);
                    await this.loadRuntimePreviewPilotState();
                } else {
                    return;
                }

                this.refreshRuntimePreviewPilotPanel();
            } catch (err) {
                showToast('RuntimePreview Pilot Console failed: ' + this.sanitizeRuntimePreviewPilotValue(err?.message || String(err)), 'error');
            }
        }
        ,
        async ensureRuntimePreviewScenarioCorpusLoaded() {
            if (Array.isArray(this.runtimePreviewScenarioCorpus?.cases) && this.runtimePreviewScenarioCorpus.cases.length > 0) {
                return;
            }

            const result = await settingsApi.loadRuntimePreviewScenarioCorpus();
            this.runtimePreviewScenarioCorpus = result?.corpus || result;
        }
        ,
        async ensureRuntimePreviewRedactedFlowCorpusLoaded() {
            if (Array.isArray(this.runtimePreviewRedactedFlowCorpus?.cases) && this.runtimePreviewRedactedFlowCorpus.cases.length > 0) {
                return;
            }

            const result = await settingsApi.loadRuntimePreviewRedactedFlowCorpus();
            this.runtimePreviewRedactedFlowCorpus = result?.corpus || result;
        }
        ,
        async ensureRuntimePreviewStationProfilesLoaded() {
            if (Array.isArray(this.runtimePreviewStationProfiles?.profiles) && this.runtimePreviewStationProfiles.profiles.length > 0) {
                return;
            }

            const result = await settingsApi.loadRuntimePreviewStationProfiles();
            this.runtimePreviewStationProfiles = result?.stationProfiles || result;
        }
        ,
        findRuntimePreviewScenarioCase(caseId) {
            const cases = Array.isArray(this.runtimePreviewScenarioCorpus?.cases)
                ? this.runtimePreviewScenarioCorpus.cases
                : [];
            return cases.find(item => String(item.caseId || '').toLowerCase() === String(caseId || '').toLowerCase()) || null;
        }
        ,
        findRuntimePreviewRedactedFlowCase(caseId) {
            const cases = Array.isArray(this.runtimePreviewRedactedFlowCorpus?.cases)
                ? this.runtimePreviewRedactedFlowCorpus.cases
                : [];
            return cases.find(item => String(item.caseId || '').toLowerCase() === String(caseId || '').toLowerCase()) || null;
        }
    });
}
