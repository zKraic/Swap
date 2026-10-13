# KraicDev comparison lab: diff of guest fingerprints.
# F0 clean (with AMD chipset packages staged), F1 after the AMD driver install and card removal, FX after a cleanup tool.
#   left      = (F1 - F0) & FX   what the AMD installation created and is still there
#   cleaned   = (F1 - F0) - FX   what the tool removed of it
#   collateral= (F0 & F1) - FX   what existed before the AMD installation and the tool removed
#   added     = FX - F1          what the tool created or changed
import json, sys, re

CATEGORIES = ['packages', 'storeFolders', 'devices', 'services', 'system32', 'syswow64', 'programDirs', 'registry', 'deviceIds', 'com', 'tasks', 'appx', 'policy']
# Values Windows rewrites on its own between two boots; never attributed to a tool.
VOLATILE = [r'\\Control\\Video\\', r'ProviderName=|DriverDate=', r'System32\\(wbem|LogFiles|Tasks|spp|perf|catroot)', r'SysWOW64\\(wbem|catroot)',
            r'\\System32\\.*\.(log|etl|dat|tmp|jrs|chk|edb)\|', r'\\System32\\(sru|WDI|SleepStudy|Microsoft\\Protect)', r'\\System32\\(config|winevt)\\']

def load(path):
    with open(path, encoding='utf-8-sig') as f:
        data = json.load(f)
    return {c: set(x for x in (data.get(c) or []) if isinstance(x, str)) for c in CATEGORIES}

def volatile(item):
    return any(re.search(p, item, re.I) for p in VOLATILE)

def diff(f0, f1, fx):
    out = {}
    for c in CATEGORIES:
        a, b, x = f0[c], f1[c], fx[c]
        created = b - a
        out[c] = {
            'created': len(created),
            'left': sorted(created & x),
            'cleaned': len(created - x),
            'collateral': sorted(i for i in (a & b) - x if not volatile(i)),
            'added': sorted(i for i in x - b if not volatile(i)),
        }
    return out

if __name__ == '__main__':
    f0, f1 = load(sys.argv[1]), load(sys.argv[2])
    policy0 = sys.argv[4] if len(sys.argv) > 4 else None
    if policy0:
        with open(policy0, encoding='utf-8-sig') as f:
            f0['policy'] = set(json.load(f))
            if not f1['policy']:
                f1['policy'] = set(f0['policy'])
    fx = load(sys.argv[3])
    result = diff(f0, f1, fx)
    json.dump(result, sys.stdout, indent=1, ensure_ascii=False)
