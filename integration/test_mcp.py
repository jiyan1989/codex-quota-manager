import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

spec=importlib.util.spec_from_file_location('manager',Path(__file__).with_name('manager_mcp.py'))
m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)

class Tests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        m.DATA=Path(self.tmp.name);m.SETTINGS=m.DATA/'settings.json';m.STATE=m.DATA/'state'/'state.json'
        m.ensure_runner=lambda:None
        self.id='00000000-0000-0000-0000-000000000001'
        m.write(m.STATE,dict(chats=[dict(id=self.id,title='Fixture',runtime='目标已暂停')]))
    def test_enable_is_queued_not_claimed_success(self):
        result=m.manage(self.id,'enable')
        self.assertTrue(result['queued']);self.assertNotIn('resumed',result)
        self.assertEqual(m.read(m.SETTINGS)['resumeThreadIds'],[self.id])
        command=m.read(next((m.DATA/'state'/'commands').glob('*.json')))
        self.assertEqual(command['action'],'AutoContinue')
        self.assertFalse(m.read(m.SETTINGS)['autoResetEnabled'])
        self.assertFalse(m.read(m.SETTINGS)['resumeAll'])
    def test_unknown_target_does_not_queue(self):
        with self.assertRaises(ValueError):m.manage('00000000-0000-0000-0000-000000000002','enable')
        self.assertFalse((m.DATA/'state'/'commands').exists())
    def test_disabled_target_removed(self):
        m.manage(self.id,'enable');m.manage(self.id,'disable')
        self.assertEqual(m.read(m.SETTINGS)['resumeThreadIds'],[])
    def test_explicit_reset_preference_survives_other_settings(self):
        m.configure(dict(autoResetEnabled=True))
        m.configure(dict(observeOnly=True))
        self.assertTrue(m.read(m.SETTINGS)['autoResetEnabled'])
        self.assertTrue(m.status()['reset_cards_enabled'])
    def test_protocol_read_only(self):
        data='\n'.join(json.dumps(x) for x in [dict(jsonrpc='2.0',id=1,method='initialize',params=dict(protocolVersion='2024-11-05')),dict(jsonrpc='2.0',id=2,method='tools/list')])+'\n'
        result=subprocess.run([sys.executable,'-X','utf8',str(Path(__file__).with_name('manager_mcp.py'))],input=data,text=True,capture_output=True,check=True,encoding='utf8')
        rows=[json.loads(x) for x in result.stdout.splitlines()]
        self.assertEqual(len(rows[1]['result']['tools']),4)

if __name__=='__main__':unittest.main()
