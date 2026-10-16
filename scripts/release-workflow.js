'use strict';

const crypto = require('crypto');

const SHA = /^[a-f0-9]{40}$/;
const CONTENT = /^[a-f0-9]{64}$/;
const VERSION = /^\d+\.\d+\.\d+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$/;
const sameRepository = (value, repository) => typeof value === 'string' && value.toLowerCase() === repository.toLowerCase();

function trustedRun(run, repository, workflow, event, successful = true) {
    if (!run || !Number.isSafeInteger(run.id) || run.id <= 0 || run.workflow_id !== workflow.id ||
        String(run.path).split('@')[0] !== workflow.path || run.head_branch !== 'main' || run.event !== event ||
        !sameRepository(run.head_repository?.full_name, repository) || !SHA.test(run.head_sha || '')) {
        throw new Error('The release input is not an own-repository main run of the exact trusted workflow.');
    }
    if (successful && (run.status !== 'completed' || run.conclusion !== 'success')) {
        throw new Error('The latest required workflow run has not completed successfully.');
    }
    return run;
}

function packageRevision(run) {
    const match = /^BGCS installed products at ([a-f0-9]{40})$/.exec(run.display_title || '');
    if (!match) throw new Error('The product run lacks its exact source-revision association.');
    return match[1];
}

function latestAccepted(runs, repository, workflow, event, revision, packageRun = false) {
    const matching = runs.filter(run => packageRun
        ? run.display_title === `BGCS installed products at ${revision}` : run.head_sha === revision);
    matching.sort((first, second) => second.id - first.id);
    if (!matching.length) throw new Error('There is no required workflow run for the exact release revision.');
    return trustedRun(matching[0], repository, workflow, event);
}

async function resolveCandidate(github, context) {
    const repository = `${context.repo.owner}/${context.repo.repo}`;
    const main = (await github.rest.git.getRef({...context.repo, ref: 'heads/main'})).data.object;
    if (main.type !== 'commit' || !SHA.test(main.sha || '')) throw new Error('The current main commit is unavailable.');
    let revision = context.sha;
    let packageRunId = null;
    if (context.eventName === 'workflow_run') {
        const incoming = context.payload.workflow_run;
        if (!incoming || !Number.isSafeInteger(incoming.id)) throw new Error('Missing triggering product run identity.');
        const workflow = (await github.rest.actions.getWorkflow({...context.repo, workflow_id: 'packages.yml'})).data;
        const actual = (await github.rest.actions.getWorkflowRun({...context.repo, run_id: incoming.id})).data;
        trustedRun(actual, repository, workflow, 'workflow_run');
        if (incoming.id !== actual.id || incoming.workflow_id !== actual.workflow_id ||
            !sameRepository(incoming.head_repository?.full_name, repository)) {
            throw new Error('The triggering event contradicts its authenticated product run.');
        }
        revision = packageRevision(actual);
        packageRunId = actual.id;
    } else if (context.eventName === 'workflow_dispatch') {
        if (context.ref !== 'refs/heads/main') throw new Error('Manual release requests must select main.');
    } else if (context.eventName === 'push') {
        if (!/^refs\/tags\/v\d+\.\d+\.\d+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$/.test(context.ref || ''))
            throw new Error('A push release request must identify a version tag.');
    } else {
        throw new Error('Unsupported release trigger.');
    }
    if (!SHA.test(revision || '')) throw new Error('The candidate release commit is invalid.');
    return {eligible: main.sha === revision, revision, packageRunId,
        reason: main.sha === revision ? 'The exact current main revision requires full artifact preflight.' : 'A newer main revision exists; no release mutation is permitted.'};
}

function validateJobs(jobs, required) {
    if (!Array.isArray(required) || !required.length || new Set(required).size !== required.length ||
        required.some(name => typeof name !== 'string' || !name.trim()) || !Array.isArray(jobs) || !jobs.length ||
        required.some(name => jobs.filter(job => job.name === name).length !== 1) ||
        jobs.some(job => job.status !== 'completed' || job.conclusion !== 'success'))
        throw new Error('Every exact required and observed workflow job must complete successfully without omissions or duplicates.');
}

function assertTagInputs({revision, version, readiness, featureBytes, packageVerification, gateManifest}) {
    if (!SHA.test(revision || '') || !VERSION.test(version || '') || readiness?.revision !== revision ||
        readiness.implementationReady !== true || readiness.featureAcceptanceVerified !== true || readiness.releaseReady !== true ||
        readiness.publicationVerified !== false || !CONTENT.test(readiness.checkedOutSourceSha256 || '') ||
        readiness.featureProofSha256 !== crypto.createHash('sha256').update(featureBytes).digest('hex') ||
        !Number.isSafeInteger(readiness.ciRunId) || readiness.ciRunId <= 0 ||
        !Number.isSafeInteger(readiness.productRunId) || readiness.productRunId <= 0 ||
        gateManifest?.workflow !== 'ci.yml' || gateManifest.productPackageWorkflow !== 'packages.yml' ||
        gateManifest.requireAllObservedJobsSuccessful !== true || gateManifest.requireCleanFeatureInventory !== true ||
        gateManifest.requireProductPackageInstallation !== true ||
        JSON.stringify(gateManifest) !== JSON.stringify(readiness.requiredGates)) {
        throw new Error('Tag creation requires the exact independently derived complete readiness proof.');
    }
    const feature = JSON.parse(featureBytes.toString('utf8'));
    if (feature.sourceRevision !== revision || feature.checkedOutSourceSha256 !== readiness.checkedOutSourceSha256 ||
        feature.featureAcceptanceVerified !== true || feature.releaseReady !== false ||
        packageVerification?.success !== true || packageVerification.completeProductSet !== true ||
        packageVerification.sourceRevision !== revision || packageVerification.sourceWorktreeDirty !== false ||
        packageVerification.sourceBytesStable !== true || packageVerification.checkedOutSourceSha256 !== readiness.checkedOutSourceSha256 ||
        packageVerification.version !== version || packageVerification.packagingEvidence?.physicalPackageDigestsVerified !== true ||
        packageVerification.packagingEvidence.sourceRevision !== revision ||
        packageVerification.packagingEvidence.sourceWorktreeDirty !== false ||
        packageVerification.packagingEvidence.sourceRevisionStable !== true ||
        packageVerification.packagingEvidence.sourceBytesStable !== true ||
        packageVerification.packagingEvidence.completeProductSet !== true) {
        throw new Error('Fresh physical package consumption and the feature proof do not certify this release source.');
    }
}

async function tagTarget(github, repository, tag) {
    let object = (await github.rest.git.getRef({...repository, ref: `tags/${tag}`})).data.object;
    const seen = new Set();
    while (object.type === 'tag') {
        if (seen.has(object.sha) || seen.size >= 8) throw new Error('The existing tag contains an invalid annotation chain.');
        seen.add(object.sha);
        object = (await github.rest.git.getTag({...repository, tag_sha: object.sha})).data.object;
    }
    if (object.type !== 'commit' || !SHA.test(object.sha || '')) throw new Error('The version tag does not identify a commit.');
    return object.sha;
}

async function ensureTag(github, repository, inputs) {
    assertTagInputs(inputs);
    // A newer failed/in-progress rerun invalidates the old green run even after preflight.
    const fullName = `${repository.owner}/${repository.repo}`;
    for (const [file, event, packageRun, expectedId, required] of [
        ['ci.yml', 'push', false, inputs.readiness.ciRunId, inputs.gateManifest.requiredJobNames],
        ['packages.yml', 'workflow_run', true, inputs.readiness.productRunId, inputs.gateManifest.requiredProductPackageJobNames]]) {
        const workflow = (await github.rest.actions.getWorkflow({...repository, workflow_id: file})).data;
        const runs = (await github.rest.actions.listWorkflowRuns({...repository, workflow_id: file, branch: 'main', event,
            ...(packageRun ? {} : {head_sha: inputs.revision}), per_page: 100})).data.workflow_runs;
        const run = latestAccepted(runs, fullName, workflow, event, inputs.revision, packageRun);
        if (run.id !== expectedId) throw new Error('The latest accepted workflow changed after release preflight.');
        const jobs = await github.paginate(github.rest.actions.listJobsForWorkflowRun, {...repository, run_id: run.id, filter: 'latest', per_page: 100});
        validateJobs(jobs, required);
    }
    const main = (await github.rest.git.getRef({...repository, ref: 'heads/main'})).data.object;
    if (main.type !== 'commit' || main.sha !== inputs.revision) throw new Error('Main advanced after preflight; do not tag or publish the stale candidate.');
    const tag = `v${inputs.version}`;
    try {
        if (await tagTarget(github, repository, tag) !== inputs.revision) throw new Error('The immutable version tag belongs to another commit.');
        return {tag, revision: inputs.revision, created: false};
    } catch (error) {
        if (error.status !== 404) throw error;
    }
    let created = false;
    try {
        const response = await github.rest.git.createRef({...repository, ref: `refs/tags/${tag}`, sha: inputs.revision});
        if (response.data.object?.type !== 'commit' || response.data.object.sha !== inputs.revision)
            throw new Error('The created version reference does not identify the accepted commit.');
        created = true;
    } catch (error) {
        if (error.status !== 409 && error.status !== 422) throw error;
        if (await tagTarget(github, repository, tag) !== inputs.revision) throw new Error('A competing version tag belongs to another commit.');
    }
    return {tag, revision: inputs.revision, created};
}

module.exports = {trustedRun, packageRevision, latestAccepted, resolveCandidate, validateJobs, assertTagInputs, ensureTag};
