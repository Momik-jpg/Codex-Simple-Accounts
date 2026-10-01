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
        self.call('initialize', {'protocolVersion': m.PROTOCOL})
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
    def test_write_failure_rolls_back(self):
        with tempfile.TemporaryDirectory() as d:
            project=Path(d)
            with mock.patch('os.replace',side_effect=OSError('disk')):
                with self.assertRaises(OSError): m.install(project)
            self.assertFalse((project/'.agents/skills/gg').exists())
            self.assertFalse((project/'.codex/config.toml').exists())
