"""Offline policy/CLI/protocol tests. No live Codex/model calls or paid requests."""
from __future__ import annotations

import copy
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
import router as r


class PolicyTests(unittest.TestCase):
    def setUp(self):
        self.p = r.load_policy(ROOT / "routing_policy.json")
        self.raw = r.load_json(ROOT / "examples/models.fixture.json")
        self.catalog = r.normalize_catalog(self.raw)
        self.simple = r.load_json(ROOT / "examples/message.json")
        self.umr = r.load_json(ROOT / "examples/umr.json")
        self.research = r.load_json(ROOT / "examples/research.json")

    def plan(self, a=None, **kw):
        return r.decide(a or self.simple, self.catalog, self.p, **kw)

    def test_simple_uses_fast_without_agents(self):
        p = self.plan()
        self.assertEqual((p['model'], p['effort'], len(p['agents'])), ('gpt-6-luna', 'low', 0))

    def test_umr_uses_deep_xhigh_and_one_later_reviewer(self):
        p = self.plan(self.umr)
        self.assertEqual((p['model'], p['effort'], len(p['agents'])), ('gpt-6-astra', 'xhigh', 1))
        self.assertEqual(p['agents'][0]['stage'], 'after_checkpoint')
        self.assertEqual(p['agents'][0]['model'], 'gpt-6-sol')

    def test_research_two_separate_agents(self):
        p = self.plan(self.research)
        self.assertEqual(p['model'], 'gpt-6-astra')
        self.assertEqual(p['max_concurrent_subagents'], 2)

    def test_standard_code_task(self):
        a = copy.deepcopy(self.simple)
        a['task_type'] = 'code'; a['workload'] = 'M'
        a['scores'].update(reasoning=2, coupling=1, failure_impact=1, verification_difficulty=1)
        p = self.plan(a)
        self.assertEqual((p['model'], p['effort']), ('gpt-6-sol', 'high'))

    def test_long_simple_batch_does_not_force_deep(self):
        a = copy.deepcopy(self.simple); a['workload'] = 'XL'
        self.assertEqual(self.plan(a)['model'], 'gpt-6-luna')

    def test_visual_gate_wins_even_with_low_sum(self):
        a = copy.deepcopy(self.simple); a['inverse_visual_reconstruction'] = True
        self.assertEqual(self.plan(a)['model'], 'gpt-6-astra')

    def test_uncertain_intake_not_fast(self):
        a = copy.deepcopy(self.simple); a['confidence'] = 'low'
        self.assertEqual(self.plan(a)['model'], 'gpt-6-sol')

    def test_high_impact_not_fast(self):
        a = copy.deepcopy(self.simple); a['scores']['failure_impact'] = 2
        self.assertEqual(self.plan(a)['model'], 'gpt-6-sol')

    def test_high_impact_difficult_verification_deep(self):
        a = copy.deepcopy(self.simple); a['scores'].update(failure_impact=3, verification_difficulty=2)
        self.assertEqual(self.plan(a)['model'], 'gpt-6-astra')

    def test_reasoning_stagnation_escalates(self):
        a = copy.deepcopy(self.simple); a.update(stagnant_attempts=2, stall_cause='reasoning')
        self.assertEqual(self.plan(a)['model'], 'gpt-6-astra')

    def test_compute_wait_does_not_escalate(self):
        a = copy.deepcopy(self.simple); a.update(stagnant_attempts=99, stall_cause='compute')
        self.assertEqual(self.plan(a)['model'], 'gpt-6-luna')

    def test_version_and_time_not_counted_as_failed_attempts(self):
        self.assertEqual(self.umr['stagnant_attempts'], 0)
        self.assertEqual(self.umr['stall_cause'], 'unknown')

    def test_missing_inputs_block_without_model(self):
        p = self.plan(r.load_json(ROOT / 'examples/blocked.json'))
        self.assertEqual(p['status'], 'blocked')
        self.assertNotIn('model', p)

    def test_user_can_disable_agents(self):
        self.assertEqual(self.plan(self.research, cap=0)['max_concurrent_subagents'], 0)

    def test_agent_cap_one(self):
        self.assertEqual(self.plan(self.research, cap=1)['max_concurrent_subagents'], 1)

    def test_small_task_never_spawns(self):
        a = copy.deepcopy(self.research); a['workload'] = 'S'
        self.assertFalse(self.plan(a)['agents'])

    def test_shared_writes_rejected(self):
        a = copy.deepcopy(self.research)
        for c in a['parallel_candidates']: c['shared_writes'] = True
        self.assertFalse(self.plan(a)['agents'])

    def test_dependencies_rejected(self):
        a = copy.deepcopy(self.research)
        for c in a['parallel_candidates']: c['independent'] = False
        self.assertFalse(self.plan(a)['agents'])

    def test_unclear_parallel_benefit_rejected(self):
        a = copy.deepcopy(self.research)
        for c in a['parallel_candidates']: c['benefit'] = 'uncertain'
        self.assertFalse(self.plan(a)['agents'])

    def test_shared_scene_allows_only_checkpoint_review(self):
        a = copy.deepcopy(self.research); a['shared_mutable_artifact'] = True
        self.assertFalse(self.plan(a)['agents'])

    def test_missing_fast_uses_balanced(self):
        cat = [x for x in self.catalog if x['model'] != 'gpt-6-luna']
        p = r.decide(self.simple, cat, self.p)
        self.assertEqual(p['model'], 'gpt-6-sol')
        self.assertEqual(p['requested_tier'], 'fast')

    def test_missing_deep_never_silently_downgrades(self):
        cat = [x for x in self.catalog if x['model'] != 'gpt-6-astra']
        with self.assertRaises(r.RouterError): r.decide(self.umr, cat, self.p)

    def test_models_not_in_catalog_never_invented(self):
        unknown = copy.deepcopy(self.raw)
        for x in unknown['data']: x['model'] += '-unknown'
        with self.assertRaises(r.RouterError): r.decide(self.umr, r.normalize_catalog(unknown), self.p)

    def test_text_only_model_rejected_for_images(self):
        cat = copy.deepcopy(self.catalog)
        for x in cat: x['modalities'] = ['text']
        with self.assertRaises(r.RouterError): r.decide(self.umr, cat, self.p)

    def test_attached_images_are_checked_independently(self):
        cat = copy.deepcopy(self.catalog)
        for x in cat: x['modalities'] = ['text']
        with self.assertRaises(r.RouterError): r.decide(self.simple, cat, self.p, attached_images=True)

    def test_unknown_modalities_use_documented_backward_compatibility(self):
        raw = copy.deepcopy(self.raw)
        for x in raw['data']: del x['inputModalities']
        self.assertIn('image', r.normalize_catalog(raw)[0]['modalities'])

    def test_missing_xhigh_uses_confirmed_high(self):
        cat = copy.deepcopy(self.catalog)
        for x in cat: x['efforts'] = ['low', 'medium', 'high']
        p = r.decide(self.umr, cat, self.p)
        self.assertEqual(p['effort'], 'high')
        self.assertTrue(any('nicht angeboten' in s for s in p['reasons']))

    def test_max_is_never_automatic_fallback(self):
        model = copy.deepcopy(self.catalog[2]); model['efforts'] = ['max']; model['default_effort'] = 'max'
        with self.assertRaises(r.RouterError): r.choose_effort(model, 'xhigh')

    def test_hidden_models_excluded(self):
        raw = copy.deepcopy(self.raw); raw['data'][2]['hidden'] = True
        self.assertNotIn('gpt-6-astra', [x['model'] for x in r.normalize_catalog(raw)])

    def test_invalid_score_rejected(self):
        a = copy.deepcopy(self.simple); a['scores']['visual'] = 4
        with self.assertRaises(r.RouterError): self.plan(a)

    def test_boolean_not_accepted_as_integer(self):
        a = copy.deepcopy(self.simple); a['scores']['visual'] = True
        with self.assertRaises(r.RouterError): self.plan(a)

    def test_extra_field_rejected(self):
        a = copy.deepcopy(self.simple); a['run_shell'] = 'unsafe'
        with self.assertRaises(r.RouterError): self.plan(a)

    def test_missing_field_rejected(self):
        a = copy.deepcopy(self.simple); del a['evidence']
        with self.assertRaises(r.RouterError): self.plan(a)

    def test_invalid_enumeration_rejected(self):
        a = copy.deepcopy(self.simple); a['workload'] = '18 hours'
        with self.assertRaises(r.RouterError): self.plan(a)

    def test_empty_catalog_rejected(self):
        with self.assertRaises(r.RouterError): r.normalize_catalog({'data': []})

    def test_invalid_policy_rejected(self):
        with tempfile.TemporaryDirectory() as td:
            p=Path(td)/'policy.json'
            bad=copy.deepcopy(self.p);bad['max_subagents']=99
            r.write_json(p,bad)
            with self.assertRaises(r.RouterError):r.load_policy(p)


class LaunchTests(unittest.TestCase):
    def setUp(self):
        p = r.load_policy(ROOT/'routing_policy.json')
        cat = r.normalize_catalog(r.load_json(ROOT/'examples/models.fixture.json'))
        self.simple = r.decide(r.load_json(ROOT/'examples/message.json'), cat, p)
        self.umr = r.decide(r.load_json(ROOT/'examples/umr.json'), cat, p)

    def test_no_agent_tools_when_none_selected(self):
        cmd = r.main_command(['codex'], self.simple, Path('/project'), Path('/logs'), Path('/logs/a.md'), [], False)
        self.assertIn('agents.enabled=false', cmd)
        self.assertIn('features.multi_agent=false', cmd)
        self.assertNotIn('danger-full-access', cmd)
        self.assertNotIn('--yolo', cmd)

    def test_analysis_stays_readonly_even_with_write_flag(self):
        cmd = r.main_command(['codex'], self.simple, Path('/project'), Path('/logs'), Path('/logs/a.md'), [], True)
        self.assertEqual(cmd[cmd.index('--sandbox')+1], 'read-only')

    def test_implementation_has_scoped_writes_and_approvals(self):
        cmd = r.main_command(['codex'], self.umr, Path('/project'), Path('/logs'), Path('/logs/a.md'), [], True)
        self.assertEqual(cmd[cmd.index('--sandbox')+1], 'workspace-write')
        self.assertEqual(cmd[cmd.index('--ask-for-approval')+1], 'on-request')
        self.assertIn('agents.max_concurrent_threads_per_session=1', cmd)
        self.assertIn('--strict-config', cmd)

    def test_intake_disables_shell_hooks_and_agents(self):
        cmd = r.triage_command(['codex'], 'gpt-6-sol', 'low', Path('/s.json'), Path('/o.json'), [])
        for flag in ['--ignore-user-config', '--ephemeral', '--strict-config', '--output-schema',
                     'features.shell_tool=false', 'features.unified_exec=false',
                     'features.hooks=false', 'agents.enabled=false']:
            self.assertIn(flag, cmd)
        self.assertEqual(cmd[-1], '-')

    def test_agent_configs_parse_and_forbid_children(self):
        try: import tomllib
        except ImportError: self.skipTest('TOML-Test benötigt Python 3.11+')
        with tempfile.TemporaryDirectory() as td:
            d=Path(td); r.prepare_execution(self.umr, 'Auftrag', d, d)
            for a in self.umr['agents']:
                config=tomllib.loads((d/f"{a['name']}.toml").read_text())
                self.assertEqual(config['model'], a['model'])
                self.assertEqual(config['sandbox_mode'], 'read-only')
                self.assertFalse(config['agents']['enabled'])
                self.assertFalse(config['features']['multi_agent'])

    def test_quotes_and_shell_metacharacters_remain_data(self):
        dangerous = 'Auftrag " & echo NO; $(false)'
        with tempfile.TemporaryDirectory() as td:
            d=Path(td); prompt=r.prepare_execution(self.simple, dangerous, d, d)
            cmd=r.main_command(['codex'], self.simple, d, d, prompt, [], False)
            self.assertIn(dangerous, prompt.read_text())
            self.assertFalse(any(dangerous in token for token in cmd))
            self.assertNotIn('sh', cmd)

    def test_cli_simulation_works_without_codex(self):
        result=subprocess.run([sys.executable, str(ROOT/'router.py'), 'demo', 'umr'], capture_output=True, text=True)
        self.assertEqual(result.returncode,0,result.stderr)
        self.assertIn('SIMULATION', result.stdout)
        self.assertIn('gpt-6-astra / xhigh', result.stdout)

    def test_plan_offline_does_not_touch_project(self):
        with tempfile.TemporaryDirectory() as td:
            d=Path(td); project=d/'project';project.mkdir();out=d/'report'
            cmd=[sys.executable,str(ROOT/'router.py'),'plan','--task','Test',
                 '--project',str(project),'--models-file',str(ROOT/'examples/models.fixture.json'),
                 '--assessment-file',str(ROOT/'examples/message.json'),'--out',str(out)]
            result=subprocess.run(cmd,capture_output=True,text=True)
            self.assertEqual(result.returncode,0,result.stderr)
            self.assertEqual(list(project.iterdir()),[])
            self.assertEqual(r.load_json(out/'plan.json')['catalog_source'],'offline_unverified')

    def test_run_rejects_unverified_catalog_argument(self):
        result=subprocess.run([sys.executable,str(ROOT/'router.py'),'run','--task','x',
                    '--models-file','made-up.json'],capture_output=True,text=True)
        self.assertNotEqual(result.returncode,0)

    def test_install_does_not_overwrite_existing_skill(self):
        with tempfile.TemporaryDirectory() as td:
            cmd=[sys.executable,str(ROOT/'router.py'),'install-skill','--project',td]
            first=subprocess.run(cmd,capture_output=True,text=True)
            self.assertEqual(first.returncode,0,first.stderr)
            p=Path(td)/'.agents/skills/task-router/SKILL.md';p.write_text('EXISTING')
            second=subprocess.run(cmd,capture_output=True,text=True)
            self.assertNotEqual(second.returncode,0)
            self.assertEqual(p.read_text(),'EXISTING')

    def test_real_subprocess_jsonrpc_handshake_and_pagination_against_fake_server(self):
        # This validates protocol framing against a local simulation, not OpenAI.
        with tempfile.TemporaryDirectory() as td:
            path=Path(td)/'fake_server.py'
            path.write_text('''import sys,json
initialized=False
for line in sys.stdin:
 m=json.loads(line)
 if m['method']=='initialize':
  print(json.dumps({'id':m['id'],'result':{'userAgent':'test'}}),flush=True)
 elif m['method']=='initialized': initialized=True
 elif m['method']=='model/list':
  if not initialized:
   print(json.dumps({'id':m['id'],'error':{'message':'not initialized'}}),flush=True)
  else:
   page=m['params'].get('cursor')
   row={'model':'gpt-6-sol' if not page else 'gpt-6-astra','hidden':False,'supportedReasoningEfforts':[{'reasoningEffort':'high'}],'defaultReasoningEffort':'high'}
   print(json.dumps({'id':m['id'],'result':{'data':[row],'nextCursor':'second' if not page else None}}),flush=True)
''')
            raw=r.discover_models([sys.executable,str(path)],timeout=5)
            self.assertEqual([x['model'] for x in raw['data']],['gpt-6-sol','gpt-6-astra'])

    def test_structured_intake_and_plan_through_simulated_codex(self):
        with tempfile.TemporaryDirectory() as td:
            d=Path(td);fake=d/'fake.py'
            fixture=str(ROOT/'examples/umr.json')
            fake.write_text(
                "import sys,json\nfrom pathlib import Path\n"
                "data=sys.stdin.read()\n"
                "assert 'AUFTRAGSDATEN' in data\n"
                "out=Path(sys.argv[sys.argv.index('--output-last-message')+1])\n"
                f"out.write_text(Path({fixture!r}).read_text())\n"
                "print(json.dumps({'type':'turn.completed','usage':{'input_tokens':123,'output_tokens':45}}))\n"
            )
            p=r.load_policy(ROOT/'routing_policy.json')
            cat=r.normalize_catalog(r.load_json(ROOT/'examples/models.fixture.json'))
            a=r.assess([sys.executable,str(fake)],cat,p,'UMR Modell rekonstruieren',[],d,10)
            plan=r.decide(a,cat,p)
            self.assertEqual(plan['model'],'gpt-6-astra')
            self.assertEqual(r.load_json(d/'intake.json')['usage_if_reported']['input_tokens'],123)
            prompt=r.prepare_execution(plan,'Originalauftrag',d,d)
            self.assertTrue(prompt.is_file())
            command=r.main_command(['codex'],plan,d,d,prompt,[],True)
            self.assertEqual(command[command.index('--model')+1],'gpt-6-astra')

    def test_subprocess_timeout_ends_intake(self):
        with tempfile.TemporaryDirectory() as td:
            with self.assertRaises(r.RouterError):
                r.captured([sys.executable,'-c','import time; time.sleep(10)'],cwd=Path(td),timeout=1)


if __name__=='__main__':
    unittest.main(verbosity=2)
