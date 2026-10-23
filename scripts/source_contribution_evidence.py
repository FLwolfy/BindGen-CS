"""Check the finite installed same-manifest source-contribution contract."""
from __future__ import annotations

import re

CHECK = 'installed-contracts-source-plugin-same-manifest-cpp-cache-and-rollback'
PREREQUISITES = (CHECK, 'installed-contracts-only-source-plugin-sdk-cli-manifest',
                 'installed-sdk-c-cpp-and-contracts-only-plugin')
SHA = re.compile(r'[0-9a-f]{64}\Z')


def verify_summary(parent: dict) -> dict:
    value = parent.get('installedSourceContributor', {})
    if (not isinstance(value, dict) or set(value) != {'receipt', 'receiptSha256', 'manifestSha256', 'pluginAssets', 'entries'}
            or value.get('receipt') != 'installed-source-contributor.json'
            or not all(check in parent.get('checks', []) for check in PREREQUISITES)
            or not all(isinstance(value.get(key), str) and SHA.fullmatch(value[key]) for key in ('receiptSha256', 'manifestSha256'))):
        raise RuntimeError('The installed source-contributor same-manifest proof is absent or incomplete.')
    assets = value.get('pluginAssets')
    names = {'Package.Plugin.dll', 'Package.Plugin.deps.json', 'BGCS.Core.dll'}
    if (not isinstance(assets, list) or len(assets) != len(names) or any(not isinstance(asset, dict) for asset in assets)
            or {asset.get('path') for asset in assets} != names
            or any(set(asset) != {'path', 'digest'} or not isinstance(asset.get('digest'), dict) or set(asset['digest']) != {'value'}
                   or not isinstance(asset['digest'].get('value'), str) or not SHA.fullmatch(asset['digest']['value']) for asset in assets)):
        raise RuntimeError('The installed source-contributor Contracts-only plugin closure is incomplete.')
    entries = value.get('entries')
    if not isinstance(entries, dict) or set(entries) != {'sdk', 'cli'}:
        raise RuntimeError('Every installed source-contributor entry point is mandatory.')
    for name, entry in entries.items():
        if (not isinstance(entry, dict) or set(entry) != {'language', 'nativeAndManagedInvoked', 'phases', 'rejectionPreservedAllFiles'}
                or entry.get('language') != 'cpp' or entry.get('nativeAndManagedInvoked') is not True
                or entry.get('rejectionPreservedAllFiles') is not True):
            raise RuntimeError('The installed source-contributor did not invoke or preserve publication at ' + name)
        phases = entry.get('phases')
        expected = (('cold', 'miss'), ('warm', 'hit'), ('changed', 'miss'))
        if not isinstance(phases, list) or len(phases) != len(expected):
            raise RuntimeError('The installed source-contributor cache phases are incomplete.')
        for phase, (label, status) in zip(phases, expected):
            if (not isinstance(phase, dict) or set(phase) != {'phase', 'emission', 'artifactCount', 'manifestSha256', 'inputRootMembershipSha256', 'otherInputFactsSha256'} or phase.get('phase') != label or phase.get('emission') != status
                    or type(phase.get('artifactCount')) is not int or phase['artifactCount'] < 5
                    or any(not isinstance(phase.get(key), str) or not SHA.fullmatch(phase[key])
                           for key in ('manifestSha256', 'inputRootMembershipSha256', 'otherInputFactsSha256'))):
                raise RuntimeError('The installed source-contributor cache/content identity is incomplete at ' + name)
        if phases[-2]['manifestSha256'] == phases[-1]['manifestSha256']:
            raise RuntimeError('The changed source-contributor state did not change its actual published artifact identity.')
    return value


def verify_report(report: dict, parent: dict, *, require_clean: bool = True) -> None:
    summary = verify_summary(parent)
    dirty = parent.get('sourceWorktreeDirty')
    revision, content = parent.get('sourceRevision'), parent.get('checkedOutSourceSha256')
    if (type(require_clean) is not bool or type(dirty) is not bool
            or not isinstance(revision, str) or not re.fullmatch(r'[0-9a-f]{40}', revision)
            or not isinstance(content, str) or not SHA.fullmatch(content)
            or require_clean and dirty is not False
            or not require_clean and parent.get('completeProductSet') is not False):
        raise RuntimeError('Only an explicitly partial local product proof may retain a matching dirty source identity.')
    source = report.get('source')
    if (report.get('schemaVersion') != 1 or report.get('success') is not True or report.get('installedProducts') is not True
            or report.get('check') != CHECK or not isinstance(source, dict)
            or type(source.get('workingTreeDirty')) is not bool
            or source != {'revision': revision, 'workingTreeDirty': dirty, 'checkedOutSourceSha256': content}
            or any(report.get(key) != summary[key] for key in ('manifestSha256', 'pluginAssets', 'entries'))):
        raise RuntimeError('The actual archived source-contributor report contradicts its source or execution summary.')
