'use strict';

// Pure API doubles: these controls never contact GitHub or mutate a repository.
const test = require('node:test');
const assert = require('node:assert/strict');
const crypto = require('crypto');
const fs = require('fs');
const path = require('path');
const release = require('./release-workflow.js');
const revision = 'a'.repeat(40);
const other = 'b'.repeat(40);
const content = 'c'.repeat(64);
const repository = {owner: 'example', repo: 'BGCS'};
const fullName = 'example/BGCS';
const workflows = {
    'ci.yml': {id: 10, path: '.github/workflows/ci.yml'},
    'packages.yml': {id: 20, path: '.github/workflows/packages.yml'}
};
const gates = {workflow: 'ci.yml', productPackageWorkflow: 'packages.yml', requireAllObservedJobsSuccessful: true,
    requireCleanFeatureInventory: true, requireProductPackageInstallation: true,
    requiredJobNames: ['Generator baseline (Windows x64)', 'Generator smoke (Linux x64)', 'Generator smoke (macOS x64)'],
    requiredProductPackageJobNames: ['Installed products across every required host']};
function run(file, id) {
    return {id, workflow_id: workflows[file].id, path: workflows[file].path, head_sha: revision,
        head_branch: 'main', event: file === 'ci.yml' ? 'push' : 'workflow_run',
        status: 'completed', conclusion: 'success', head_repository: {full_name: fullName},
        display_title: `BGCS installed products at ${revision}`};
}
function inputs() {
    const featureBytes = Buffer.from(JSON.stringify({sourceRevision: revision, checkedOutSourceSha256: content,
        featureAcceptanceVerified: true, releaseReady: false}));
    return {revision, version: '3.0.0', featureBytes, gateManifest: structuredClone(gates), readiness: {
        revision, implementationReady: true, featureAcceptanceVerified: true, releaseReady: true, publicationVerified: false,
        checkedOutSourceSha256: content, featureProofSha256: crypto.createHash('sha256').update(featureBytes).digest('hex'),
        ciRunId: 100, productRunId: 200, requiredGates: structuredClone(gates)},
        packageVerification: {success: true, completeProductSet: true, sourceRevision: revision,
            sourceWorktreeDirty: false, sourceBytesStable: true, checkedOutSourceSha256: content, version: '3.0.0',
            packagingEvidence: {physicalPackageDigestsVerified: true, sourceRevision: revision, sourceWorktreeDirty: false,
                sourceRevisionStable: true, sourceBytesStable: true, completeProductSet: true}}};
}
function api(options = {}) {
    let mutations = 0;
    const ci = options.ci || [run('ci.yml', 100)], products = options.products || [run('packages.yml', 200)];
    const jobs = (names) => names.map(name => ({name, status: 'completed', conclusion: 'success'}));
    const github = {rest: {actions: {
        getWorkflow: async ({workflow_id}) => ({data: workflows[workflow_id]}),
        getWorkflowRun: async () => ({data: products[0]}),
        listWorkflowRuns: async ({workflow_id}) => ({data: {workflow_runs: workflow_id === 'ci.yml' ? ci : products}}),
        listJobsForWorkflowRun: () => {throw Error('paginate must be used');}
    }, git: {
        getRef: async ({ref}) => {
            if (ref === 'heads/main') return {data: {object: {type: 'commit', sha: options.main || revision}}};
            if (options.raceTarget && mutations) return {data: {object: {type: 'commit', sha: options.raceTarget}}};
            if (options.tag) return {data: {object: options.tag}};
            throw Object.assign(Error('missing tag'), {status: 404});
        },
        getTag: async () => ({data: {object: options.annotatedTarget}}),
        createRef: async value => {
            mutations++;
            assert.equal(value.ref, 'refs/tags/v3.0.0'); assert.equal(value.sha, revision);
            if (options.createError) throw options.createError;
            return {data: {object: {type: 'commit', sha: options.created || revision}}};
        }
    }}, paginate: async (_, {run_id}) => options.jobs || jobs(run_id === 100 ? gates.requiredJobNames : gates.requiredProductPackageJobNames)};
    return {github, get mutations() {return mutations;}};
}

test('own main exact trusted workflow is accepted', () => {
    assert.equal(release.trustedRun(run('ci.yml', 100), fullName, workflows['ci.yml'], 'push').id, 100);
});
for (const [name, change] of Object.entries({fork: {head_repository: {full_name: 'fork/BGCS'}}, branch: {head_branch: 'feature'},
    event: {event: 'pull_request'}, workflow: {workflow_id: 999}, path: {path: '.github/workflows/untrusted.yml'},
    cancelled: {conclusion: 'cancelled'}, running: {status: 'in_progress'}, noCommit: {head_sha: ''}})) {
    test(`reject ${name} run provenance`, () => assert.throws(() => release.trustedRun({...run('ci.yml', 100), ...change}, fullName, workflows['ci.yml'], 'push')));
}
test('newer failed or running rerun cannot be replaced by historical green', () => {
    for (const change of [{conclusion: 'failure'}, {status: 'in_progress', conclusion: null}])
        assert.throws(() => release.latestAccepted([run('ci.yml', 100), {...run('ci.yml', 101), ...change}], fullName, workflows['ci.yml'], 'push', revision));
});
test('automatic candidate uses authenticated product title, not workflow_run default-branch SHA', async () => {
    const mock = api({products: [{...run('packages.yml', 200), head_sha: other}]});
    const result = await release.resolveCandidate(mock.github, {repo: repository, sha: other, eventName: 'workflow_run', payload: {workflow_run: {...run('packages.yml', 200), head_sha: other}}});
    assert.deepEqual([result.eligible, result.revision, result.packageRunId], [true, revision, 200]); assert.equal(mock.mutations, 0);
});
test('stale candidate has no mutation authorization', async () => {
    const mock = api({main: other});
    const result = await release.resolveCandidate(mock.github, {repo: repository, sha: revision, eventName: 'workflow_dispatch', ref: 'refs/heads/main'});
    assert.equal(result.eligible, false); assert.equal(mock.mutations, 0);
});
test('manual non-main or unsupported event cannot request publication', async () => {
    for (const context of [{eventName: 'workflow_dispatch', ref: 'refs/heads/feature'}, {eventName: 'pull_request'}, {eventName: 'push', ref: 'refs/heads/main'}])
        await assert.rejects(() => release.resolveCandidate(api().github, {repo: repository, sha: revision, ...context}));
});
test('mismatched event and API identity is rejected', async () => {
    await assert.rejects(() => release.resolveCandidate(api().github, {repo: repository, eventName: 'workflow_run',
        payload: {workflow_run: {...run('packages.yml', 200), workflow_id: 999}}}));
});
test('every required and observed job must be uniquely complete', () => {
    const valid = [{name: 'required', status: 'completed', conclusion: 'success'}];
    release.validateJobs(valid, ['required']);
    for (const rows of [[], [...valid, ...valid], [{...valid[0], status: 'queued'}], [...valid, {name: 'other', status: 'completed', conclusion: 'skipped'}]])
        assert.throws(() => release.validateJobs(rows, ['required']));
});
test('tag requires derived clean feature and complete fresh physical proofs', () => {
    release.assertTagInputs(inputs());
    for (const mutate of [v => v.readiness.releaseReady = false, v => v.readiness.publicationVerified = true,
        v => v.readiness.featureProofSha256 = 'd'.repeat(64), v => v.featureBytes = Buffer.from('{}'),
        v => v.packageVerification.sourceWorktreeDirty = true, v => v.packageVerification.completeProductSet = false,
        v => v.packageVerification.packagingEvidence.physicalPackageDigestsVerified = false,
        v => v.packageVerification.packagingEvidence.sourceBytesStable = false,
        v => v.gateManifest.requiredJobNames.pop(), v => v.readiness.productRunId = null,
        v => v.packageVerification.version = '3.0.1']) {
        const value = inputs(); mutate(value); assert.throws(() => release.assertTagInputs(value));
    }
});
test('accepted exact current main creates one tag for continued same-job publication', async () => {
    const mock = api(); const result = await release.ensureTag(mock.github, repository, inputs());
    assert.deepEqual(result, {tag: 'v3.0.0', revision, created: true}); assert.equal(mock.mutations, 1);
});
test('main advancement and newer failed product run are rejected before tag mutation', async () => {
    for (const options of [{main: other}, {products: [{...run('packages.yml', 201), conclusion: 'failure'}, run('packages.yml', 200)]},
        {products: [run('packages.yml', 201)]}, {ci: [run('ci.yml', 101)]}, {jobs: [{name: 'unrelated', status: 'completed', conclusion: 'success'}]}]) {
        const mock = api(options); await assert.rejects(() => release.ensureTag(mock.github, repository, inputs())); assert.equal(mock.mutations, 0);
    }
});
test('existing same lightweight or annotated tag is idempotent without rewriting', async () => {
    for (const options of [{tag: {type: 'commit', sha: revision}}, {tag: {type: 'tag', sha: other}, annotatedTarget: {type: 'commit', sha: revision}}]) {
        const mock = api(options); assert.equal((await release.ensureTag(mock.github, repository, inputs())).created, false); assert.equal(mock.mutations, 0);
    }
});
test('conflicting or cyclic existing tag is never overwritten', async () => {
    for (const options of [{tag: {type: 'commit', sha: other}}, {tag: {type: 'tag', sha: other}, annotatedTarget: {type: 'tag', sha: other}}]) {
        const mock = api(options); await assert.rejects(() => release.ensureTag(mock.github, repository, inputs())); assert.equal(mock.mutations, 0);
    }
});
test('API denial propagates without bypassing publication gates', async () => {
    const mock = api({createError: Object.assign(Error('denied'), {status: 403})});
    await assert.rejects(() => release.ensureTag(mock.github, repository, inputs()), /denied/);
});
test('tag creation race only accepts the identical immutable commit', async () => {
    const same = api({raceTarget: revision, createError: Object.assign(Error('exists'), {status: 422})});
    assert.equal((await release.ensureTag(same.github, repository, inputs())).created, false);
    const conflict = api({raceTarget: other, createError: Object.assign(Error('exists'), {status: 409})});
    await assert.rejects(() => release.ensureTag(conflict.github, repository, inputs()), /competing version/);
});

test('workflow keeps authentication, immutable tag, NuGet and public verification in order', () => {
    const workflow = fs.readFileSync(path.join(__dirname, '../.github/workflows/publish-bgcs-runtime-nuget.yml'), 'utf8');
    assert.match(workflow, /workflow_run:\s+workflows: \["Product package certification"\]/);
    assert.match(workflow, /github\.event\.workflow_run\.event == 'workflow_run'/);
    assert.match(workflow, /github\.event\.workflow_run\.head_repository\.full_name == github\.repository/);
    assert.match(workflow, /release\.resolveCandidate\(github, context\)/);
    assert.doesNotMatch(workflow, /dotnet test|run-host-test-shards\.py|test-nuget-packages\.sh/);
    const ordered = ['Require the full CI matrix', 'Download accepted product packages', 'Recheck physical package hashes',
        'release.ensureTag(github, context.repo', 'Exchange GitHub OIDC token for a nuget.org API key',
        'Push packages and symbols', 'verify-published-products.py', 'Publish GitHub release'];
    let previous = -1;
    for (const marker of ordered) {
        const current = workflow.indexOf(marker); assert.ok(current > previous, marker); previous = current;
    }
    // Parse every github-script body as an async function, without executing any API.
    const AsyncFunction = Object.getPrototypeOf(async function () {}).constructor;
    for (const match of workflow.matchAll(/^          script: \|\r?\n((?:            [^\n]*(?:\n|$)|\r?\n)+)/gm)) {
        const source = match[1].split(/\r?\n/).map(line => line.startsWith('            ') ? line.slice(12) : line).join('\n');
        assert.doesNotThrow(() => new AsyncFunction('require', 'github', 'context', 'core', source));
    }
});
