"""Offline bridge regressions; no paid calls."""
import copy
import io
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest import mock
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
import router as r
import mcp_server as m

class BridgeTests(unittest.TestCase):
    def setUp(self):
        self.server = m.Server()
        self.call('initialize', {'protocolVersion': m.PROTOCOL, 'capabilities': {}, 'clientInfo': {'name': 'test', 'version': '1'}})
        self.server.handle({'jsonrpc': '2.0', 'method': 'notifications/initialized'})
    def call(self, method, params=None):
        return self.server.handle({'jsonrpc': '2.0', 'id': 1, 'method': method, 'params': params or {}})
    def test_catalog(self):
        self.assertEqual(self.call('tools/list')['result']['tools'], [m.TOOL])
    def test_requires_handshake(self):
        self.assertIn('error', m.Server().handle({'jsonrpc':'2.0','id':1,'method':'tools/list'}))
    def test_discovery_probe(self):
        self.assertEqual(self.call('server/discover')['error']['code'], -32601)
    def test_unknown_tool(self):
        self.assertEqual(self.call('tools/call', {'name':'run'})['error']['code'], -32602)
    def test_invalid_request(self):
        for request in ([], {}, {'jsonrpc':'2.0','id':True,'method':'ping'}):
            self.assertEqual(self.server.handle(request)['error']['code'], -32600)
    def test_parse_then_ping(self):
        out = io.StringIO()
        m.serve(io.StringIO('broken\n{"jsonrpc":"2.0","id":9,"method":"ping"}\n'), out)
        results = [json.loads(x) for x in out.getvalue().splitlines()]
        self.assertEqual(results[0]['error']['code'], -32700)
        self.assertEqual(results[1]['result'], {})
    def test_oversized(self):
        out = io.StringIO()
        m.serve(io.StringIO('x' * (m.MAX_MESSAGE+1)), out)
        self.assertEqual(json.loads(out.getvalue())['error']['code'], -32600)
    def test_process_stdio(self):
        result = subprocess.run([sys.executable,str(ROOT/'mcp_server.py')], input='{"jsonrpc":"2.0","id":7,"method":"ping"}\n',capture_output=True,text=True,timeout=10)
        self.assertEqual(result.returncode,0)
        self.assertEqual(json.loads(result.stdout)['id'],7)
        self.assertEqual(result.stderr,'')
    def test_error_redacts_diagnostics(self):
        with mock.patch.object(m,'evaluate',side_effect=r.RouterError('SECRET')):
            response=self.call('tools/call',{'name':'evaluate_task','arguments':{'task':'t'}})
        self.assertTrue(response['result']['isError'])
        self.assertNotIn('SECRET',json.dumps(response))
        self.assertNotIn('structuredContent',response['result'])
    def test_invalid_input_no_process(self):
        with mock.patch.object(r,'resolve_codex') as resolve:
            for args in ({'task':''},{'task':'t','codex':'evil'},{'task':'t','max_subagents':True},{'task':'t','max_subagents':3},{'task':'x'*30000,'chat_context':'x'*30000}):
                with self.assertRaises(r.RouterError): m.evaluate(args)
            resolve.assert_not_called()
    def test_invalid_id_is_not_reflected(self):
        response=self.server.handle({'jsonrpc':'2.0','id':{'secret':'value'},'method':'ping'})
        self.assertEqual(response['error']['code'],-32600)
        self.assertIsNone(response['id'])
        self.assertNotIn('secret',json.dumps(response))
    def test_malformed_initialization_does_not_advance_session(self):
        server=m.Server()
        response=server.handle({'jsonrpc':'2.0','id':1,'method':'initialize','params':{'protocolVersion':m.PROTOCOL}})
        self.assertEqual(response['error']['code'],-32602)
        self.assertFalse(server.initialized)
    def test_non_object_tool_arguments_are_protocol_error(self):
        with mock.patch.object(m,'evaluate') as evaluate:
            result=self.call('tools/call',{'name':'evaluate_task','arguments':[]})
            self.assertEqual(result['error']['code'],-32602)
            evaluate.assert_not_called()
    def test_nonstandard_json_and_duplicate_keys_rejected(self):
        for text in ('{"jsonrpc":"2.0","id":NaN,"method":"ping"}',
                     '{"jsonrpc":"2.0","id":1,"method":"ping","method":"tools/call"}'):
            out=io.StringIO(); m.serve(io.StringIO(text+'\n'),out)
            self.assertEqual(json.loads(out.getvalue())['error']['code'],-32700)
    def test_surrogate_id_does_not_break_utf8_transport(self):
        out=io.StringIO()
        m.serve(io.StringIO('{"jsonrpc":"2.0","id":"\\ud800","method":"ping"}\n'),out)
        out.getvalue().encode('utf-8')
        self.assertEqual(json.loads(out.getvalue())['result'],{})
    def test_actionable_validation_error_without_input_leak(self):
        result=self.call('tools/call',{'name':'evaluate_task','arguments':{'task':'SECRET','extra':'SECRET'}})['result']
        self.assertTrue(result['isError'])
        self.assertEqual(result['_meta']['error_code'],'INVALID_INPUT')
        self.assertNotIn('SECRET',json.dumps(result))
    def test_missing_authentication_has_safe_error_code(self):
        with mock.patch.object(r,'credential_fingerprint',side_effect=r.RouterError('SECRET')):
            result=self.call('tools/call',{'name':'evaluate_task','arguments':{'task':'t'}})['result']
        self.assertEqual(result['_meta']['error_code'],'AUTH_UNAVAILABLE')
        self.assertNotIn('SECRET',json.dumps(result))
    def test_router_diagnostics_are_not_exposed_in_stderr_or_stdout(self):
        def evaluate(*args):
            print('SECRET')
            print('SECRET',file=sys.stderr)
            raise r.RouterError('SECRET')
        out=io.StringIO(); err=io.StringIO()
        with mock.patch.object(m,'evaluate',side_effect=evaluate), mock.patch('sys.stdout',out), mock.patch('sys.stderr',err):
            result=self.call('tools/call',{'name':'evaluate_task','arguments':{'task':'t'}})
        self.assertEqual(out.getvalue(),'')
        self.assertEqual(err.getvalue(),'')
        self.assertEqual(result['result']['_meta']['error_code'],'EVALUATION_FAILED')
    def evaluated(self, fingerprints):
        assessment=copy.deepcopy(r.load_json(ROOT/'examples/message.json'))
        assessment['confidence']='low'
        def assess(*args):
            self.assertIn('accepted constraint',args[3])
            r.write_json(args[5]/'intake.json',{'model':'actual-classifier','effort':'medium'})
            return assessment
        with mock.patch.object(r,'credential_fingerprint',side_effect=fingerprints), mock.patch.object(r,'resolve_codex',return_value=['codex']), mock.patch.object(r,'discover_models',return_value=r.load_json(ROOT/'examples/models.fixture.json')), mock.patch.object(r,'assess',side_effect=assess):
            return m.evaluate({'task':'message','chat_context':'accepted constraint'})
    def test_shared_rules_and_actual_classifier(self):
        plan=self.evaluated(['same','same'])
        self.assertEqual(plan['safety_floor'],'balanced')
        self.assertEqual(plan['effort'],'medium')
        self.assertEqual(plan['intake']['model'],'actual-classifier')
        self.assertEqual(plan['chat_execution']['parent'],'current_chat')
        self.assertEqual(plan['chat_execution']['agents'],'proposed_not_started')
    def test_account_change_discards_plan(self):
        with self.assertRaisesRegex(r.RouterError,'Konto'): self.evaluated(['before','after'])

class InstallationTests(unittest.TestCase):
    def test_preserves_config_refuses_overwrite(self):
        import tomllib
        with tempfile.TemporaryDirectory() as d:
            project=Path(d); config=project/'.codex/config.toml'; config.parent.mkdir()
            original='model = "existing"\n[mcp_servers.other]\ncommand = "other"\n'
            config.write_text(original,encoding='utf-8'); m.install(project)
            value=config.read_text(encoding='utf-8'); self.assertTrue(value.startswith(original))
            parsed=tomllib.loads(value)
            self.assertEqual(parsed['model'],'existing')
            self.assertEqual(parsed['mcp_servers']['simple_accounts_router']['tool_timeout_sec'],210)
            self.assertTrue((project/'.agents/skills/gg/SKILL.md').is_file())
            with self.assertRaises(r.RouterError): m.install(project)
            self.assertEqual(config.read_text(encoding='utf-8'),value)
    def test_malformed_config_untouched(self):
        with tempfile.TemporaryDirectory() as d:
            project=Path(d); config=project/'.codex/config.toml'; config.parent.mkdir()
            config.write_text('[broken',encoding='utf-8')
            with self.assertRaises(ValueError): m.install(project)
            self.assertFalse((project/'.agents/skills/gg').exists())
            self.assertEqual(config.read_text(encoding='utf-8'),'[broken')
    @unittest.skipIf(sys.platform == 'win32', 'Windows symlinks may require elevated rights')
    def test_symlink_config_cannot_change_external_settings(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d); project=root/'project'; project.mkdir()
            external=root/'settings'; external.mkdir()
            config=external/'config.toml'; config.write_text('model="keep"',encoding='utf-8')
            (project/'.codex').symlink_to(external, target_is_directory=True)
            with self.assertRaisesRegex(r.RouterError,'symbolische'):
                m.install(project)
            self.assertEqual(config.read_text(encoding='utf-8'),'model="keep"')
            self.assertFalse((project/'.agents/skills/gg').exists())
    def test_partial_skill_copy_is_cleaned_up(self):
        def failing_copy(source,destination):
            destination.mkdir()
            (destination/'partial.txt').write_text('partial')
            raise OSError('copy failed')
        with tempfile.TemporaryDirectory() as d:
            project=Path(d)
            with mock.patch('shutil.copytree',side_effect=failing_copy):
                with self.assertRaises(OSError): m.install(project)
            self.assertFalse((project/'.agents/skills/gg').exists())
            self.assertEqual(list((project/'.agents/skills').iterdir()),[])
            self.assertFalse((project/'.codex/config.toml').exists())
            self.assertFalse((project/'.codex/.simple-accounts-router-setup.lock').exists())
    def test_setup_lock_refuses_parallel_installation(self):
        with tempfile.TemporaryDirectory() as d:
            project=Path(d); directory=project/'.codex'; directory.mkdir()
            lock=directory/'.simple-accounts-router-setup.lock'; lock.write_text('other process')
            with self.assertRaisesRegex(r.RouterError,'bereits aktiv'): m.install(project)
            self.assertEqual(lock.read_text(),'other process')
            self.assertFalse((project/'.agents/skills/gg').exists())
    def test_existing_settings_changed_during_copy_are_preserved(self):
        import shutil
        copytree=shutil.copytree
        with tempfile.TemporaryDirectory() as d:
            project=Path(d); config=project/'.codex/config.toml'; config.parent.mkdir()
            config.write_text('model="before"',encoding='utf-8')
            def changing_copy(source,destination):
                result=copytree(source,destination)
                config.write_text('model="concurrent"',encoding='utf-8')
                return result
            with mock.patch('shutil.copytree',side_effect=changing_copy):
                with self.assertRaisesRegex(r.RouterError,'gleichzeitig'): m.install(project)
            self.assertEqual(config.read_text(encoding='utf-8'),'model="concurrent"')
            self.assertFalse((project/'.agents/skills/gg').exists())
    def test_scalar_mcp_servers_refused_cleanly(self):
        with tempfile.TemporaryDirectory() as d:
            project=Path(d); config=project/'.codex/config.toml'; config.parent.mkdir()
            config.write_text('mcp_servers=2',encoding='utf-8')
            with self.assertRaisesRegex(r.RouterError,'TOML-Tabelle'): m.install(project)
            self.assertEqual(config.read_text(encoding='utf-8'),'mcp_servers=2')
            self.assertFalse((project/'.agents/skills/gg').exists())
    def test_windows_junction_detection(self):
        path=Path('junction')
        attributes=mock.Mock(st_file_attributes=0x400)
        with mock.patch.object(Path,'lstat',return_value=attributes), mock.patch.object(Path,'is_symlink',return_value=False):
            with self.assertRaisesRegex(r.RouterError,'Junctions'): m.reject_redirected_paths((path,))
    def test_write_failure_rolls_back(self):
        with tempfile.TemporaryDirectory() as d:
            project=Path(d)
            with mock.patch('os.replace',side_effect=OSError('disk')):
                with self.assertRaises(OSError): m.install(project)
            self.assertFalse((project/'.agents/skills/gg').exists())
            self.assertFalse((project/'.codex/config.toml').exists())
