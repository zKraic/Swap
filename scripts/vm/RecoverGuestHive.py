"""KraicDev: replay transaction logs of the private VM SYSTEM hive, never a host hive."""
import pathlib
import shutil
import sys

lab = pathlib.Path('D:/KraicDev/Swap-VM').resolve()
directory = pathlib.Path(sys.argv[1]).resolve()
if not directory.is_relative_to(lab) or not directory.name.endswith('.vhd.hives'):
    raise ValueError('Only a private guest hive export is accepted')
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[2] / 'artifacts/tools/yarp'))
from yarp import Registry

for name in ['SYSTEM', 'SOFTWARE']:
    with (directory / name).open('rb') as primary, (directory / (name + '.LOG1')).open('rb') as log1, (directory / (name + '.LOG2')).open('rb') as log2:
        hive = Registry.RegistryHive(primary)
        result = hive.recover_auto(None, log1, log2)
        if result.recovered:
            hive.save_recovered_hive(str(directory / (name + '.recovered')))
        else:
            # A normal shutdown leaves a clean hive; no transaction replay is required.
            shutil.copyfile(directory / name, directory / (name + '.recovered'))
        print('KraicDev: guest', name, 'transaction logs recovered:', result.recovered)
