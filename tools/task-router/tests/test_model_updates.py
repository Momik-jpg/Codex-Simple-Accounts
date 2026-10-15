"""Release discovery regressions using synthetic catalogs; no inference or credentials."""
import copy
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
import router as r
import mcp_server as bridge


class ModelUpdateTests(unittest.TestCase):
    def setUp(self):
        self.policy = r.load_policy(ROOT / 'routing_policy.json')
        self.raw = r.load_json(ROOT / 'examples/models.fixture.json')
        self.catalog = r.normalize_catalog(self.raw)
        self.task = r.load_json(ROOT / 'examples/message.json')
        self.task['confidence'] = 'low'

    def release(self, name, base='gpt-6-sol', **changes):
        model = copy.deepcopy(next(item for item in self.catalog if item['model'] == base))
        model.update(model=name, **changes)
        return model

    def plan(self, catalog=None, **options):
        return r.decide(self.task, self.catalog if catalog is None else catalog, self.policy, **options)

    def test_new_sol_is_used_by_main_selection_and_classifier(self):
        catalog = self.catalog + [self.release('gpt-6.1-sol')]
        plan = self.plan(catalog)
        classifier, effort, _ = r.choose_classifier_model(catalog, self.policy, False)
        self.assertEqual((plan['model'], classifier['model'], effort), ('gpt-6.1-sol', 'gpt-6.1-sol', 'low'))
        self.assertEqual(plan['model_selection'], {'source': 'live_family_version', 'policy_model': 'gpt-6-sol'})
        self.assertTrue(any('kein gemessener Leistungsvorteil' in reason for reason in plan['reasons']))

    def test_new_luna_astra_and_review_models_keep_their_roles(self):
        catalog = self.catalog + [self.release('gpt-6.1-luna', 'gpt-6-luna'),
                                  self.release('gpt-6.1-astra', 'gpt-6-astra'),
                                  self.release('gpt-6.1-sol')]
        simple = r.load_json(ROOT / 'examples/message.json')
        deep = r.load_json(ROOT / 'examples/umr.json')
        self.assertEqual(r.decide(simple, catalog, self.policy)['model'], 'gpt-6.1-luna')
        plan = r.decide(deep, catalog, self.policy)
        self.assertEqual((plan['model'], plan['effort']), ('gpt-6.1-astra', 'xhigh'))
        self.assertEqual(plan['agents'][0]['model'], 'gpt-6.1-sol')

    def test_versions_are_numeric_and_independent_of_catalog_order(self):
        catalog = self.catalog + [self.release('gpt-6.9-sol'), self.release('gpt-6.10-sol')]
        for order in (catalog, list(reversed(catalog))):
            self.assertEqual(self.plan(order)['model'], 'gpt-6.10-sol')

    def test_future_major_version_is_adopted_without_policy_edits(self):
        catalog = self.catalog + [self.release('gpt-6.99-sol'), self.release('gpt-7-sol')]
        self.assertEqual(self.plan(catalog)['model'], 'gpt-7-sol')

    def test_unknown_families_variants_and_snapshots_are_not_auto_approved(self):
        for name in ('gpt-7-newfamily', 'gpt-6.2-sol-preview', 'gpt-6.2-sol-2026-10-02',
                     'provider/gpt-7-sol', 'gpt-6.2-sol-pro', 'gpt-06.2-sol'):
            with self.subTest(name=name):
                catalog = self.catalog + [self.release(name)]
                self.assertEqual(self.plan(catalog)['model'], 'gpt-6-sol')
                with self.assertRaisesRegex(r.RouterError, 'Qualitätsstufe'):
                    self.plan(catalog, model_preference=name)

    def test_hidden_release_is_not_selected(self):
        raw = copy.deepcopy(self.raw)
        hidden = copy.deepcopy(raw['data'][1])
        hidden.update(model='gpt-7-sol', hidden=True)
        raw['data'].append(hidden)
        catalog = r.normalize_catalog(raw)
        self.assertEqual(self.plan(catalog)['model'], 'gpt-6-sol')
        with self.assertRaisesRegex(r.RouterError, 'Live-Katalog'):
            self.plan(catalog, model_preference='gpt-7-sol')

    def test_new_release_without_images_falls_back_to_capable_old_release(self):
        catalog = self.catalog + [self.release('gpt-6.1-sol', modalities=['text'])]
        self.assertEqual(self.plan(catalog, attached_images=True)['model'], 'gpt-6-sol')
        classifier, _, _ = r.choose_classifier_model(catalog, self.policy, True)
        self.assertEqual(classifier['model'], 'gpt-6-sol')

    def test_new_release_cannot_lower_the_medium_safety_floor(self):
        model = self.release('gpt-6.1-sol', efforts=['low'])
        self.assertEqual(self.plan(self.catalog + [model])['model'], 'gpt-6-sol')
        with self.assertRaisesRegex(r.RouterError, 'mindestens medium'):
            self.plan([model])

    def test_new_release_with_only_max_or_ultra_is_not_an_automatic_upgrade(self):
        for effort in ('max', 'ultra'):
            with self.subTest(effort=effort):
                catalog = self.catalog + [self.release('gpt-6.1-sol', efforts=[effort])]
                self.assertEqual(self.plan(catalog)['model'], 'gpt-6-sol')
                plan = self.plan(catalog, model_preference='gpt-6.1-sol', effort_preference=effort)
                self.assertEqual((plan['model'], plan['effort']), ('gpt-6.1-sol', effort))

    def test_manual_pin_is_retained_and_new_manual_release_keeps_safety_floor(self):
        catalog = self.catalog + [self.release('gpt-6.1-sol')]
        self.assertEqual(self.plan(catalog, model_preference='gpt-6-sol')['model'], 'gpt-6-sol')
        plan = self.plan(catalog, model_preference='gpt-6.1-sol', effort_preference='low')
        self.assertEqual((plan['model'], plan['effort'], plan['safety_floor']), ('gpt-6.1-sol', 'medium', 'balanced'))

    def test_new_luna_cannot_bypass_safety_floor(self):
        catalog = self.catalog + [self.release('gpt-6.1-luna', 'gpt-6-luna')]
        with self.assertRaisesRegex(r.RouterError, 'mindestens balanced'):
            self.plan(catalog, model_preference='gpt-6.1-luna')

    def test_explicit_tier_assignment_overrides_family_inheritance(self):
        self.policy['models']['fast'].insert(0, 'gpt-6.1-sol')
        catalog = self.catalog + [self.release('gpt-6.1-sol')]
        self.assertEqual(self.plan(catalog)['model'], 'gpt-6-sol')
        with self.assertRaisesRegex(r.RouterError, 'mindestens balanced'):
            self.plan(catalog, model_preference='gpt-6.1-sol')

    def test_legacy_or_disabled_policy_remains_pinned(self):
        catalog = self.catalog + [self.release('gpt-6.1-sol')]
        for value in (False, None):
            if value is None:
                self.policy.pop('auto_model_versions', None)
            else:
                self.policy['auto_model_versions'] = value
            with self.subTest(value=value):
                self.assertEqual(self.plan(catalog)['model'], 'gpt-6-sol')
                with self.assertRaisesRegex(r.RouterError, 'Qualitätsstufe'):
                    self.plan(catalog, model_preference='gpt-6.1-sol')

    def test_custom_classifier_preference_is_preserved(self):
        self.policy['classifier_models'] = ['gpt-5.5']
        catalog = self.catalog + [self.release('gpt-6.1-sol'), self.release('gpt-5.5')]
        classifier, _, _ = r.choose_classifier_model(catalog, self.policy, False)
        self.assertEqual(classifier['model'], 'gpt-5.5')

    def test_releases_below_approved_seed_versions_are_not_adopted(self):
        with self.assertRaisesRegex(r.RouterError, 'Kein freigegebenes Modell'):
            self.plan([self.release('gpt-5.0-sol')])

    def test_invalid_discovery_flags_are_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'policy.json'
            for value in ('true', 1, None, [], {}):
                policy = copy.deepcopy(self.policy)
                policy['auto_model_versions'] = value
                r.write_json(path, policy)
                with self.subTest(value=value), self.assertRaises(r.RouterError):
                    r.load_policy(path)

    def test_gg_bridge_uses_new_release_for_real_classification_and_decision(self):
        raw = copy.deepcopy(self.raw)
        new = copy.deepcopy(raw['data'][1])
        new['model'] = 'gpt-6.1-sol'
        raw['data'].append(new)
        def captured(command, **kwargs):
            self.assertEqual(command[command.index('--model') + 1], 'gpt-6.1-sol')
            self.assertIn('accepted context', kwargs['input_text'])
            r.write_json(Path(command[command.index('--output-last-message') + 1]), self.task)
            return '', ''
        with mock.patch.object(r, 'credential_fingerprint', return_value='fixture'), \
             mock.patch.object(r, 'resolve_codex', return_value=['fixture-codex']), \
             mock.patch.object(r, 'discover_models', return_value=raw), \
             mock.patch.object(r, 'captured', side_effect=captured):
            plan = bridge.evaluate({'task': 'task', 'chat_context': 'accepted context'})
        self.assertEqual((plan['model'], plan['intake']['model']), ('gpt-6.1-sol', 'gpt-6.1-sol'))
        self.assertEqual((plan['safety_floor'], plan['effort']), ('balanced', 'medium'))
        self.assertEqual(plan['chat_execution']['main_model'], 'recommendation_only_no_switch_performed')

    def test_new_release_arriving_before_launch_requires_updated_preview(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            project = root / 'project'; project.mkdir()
            home = root / 'home'; home.mkdir()
            (home / 'auth.json').write_text('{"fixture": true}', encoding='utf-8')
            assessment = root / 'assessment.json'
            r.write_json(assessment, self.task)
            second = copy.deepcopy(self.raw)
            new = copy.deepcopy(second['data'][1]); new['model'] = 'gpt-6.1-sol'
            second['data'].append(new)
            argv = [str(ROOT / 'router.py'), 'run', '--task', 'Inspect only', '--project', str(project),
                    '--assessment-file', str(assessment), '--out', str(root / 'run')]
            with mock.patch.object(sys, 'argv', argv), \
                 mock.patch.dict(os.environ, {'CODEX_HOME': str(home)}), \
                 mock.patch.object(r, 'resolve_codex', return_value=['fixture-codex']), \
                 mock.patch.object(r, 'discover_models', side_effect=[self.raw, second]), \
                 mock.patch.object(subprocess, 'call') as launch:
                with self.assertRaisesRegex(r.RouterError, 'gpt-6.1-sol'):
                    r.cli()
                launch.assert_not_called()

    def test_agent_upgrade_fallback_records_the_actual_deep_policy(self):
        catalog = [self.release('gpt-6.1-astra', 'gpt-6-astra')]
        plan = r.decide(r.load_json(ROOT / 'examples/umr.json'), catalog, self.policy)
        self.assertEqual(plan['agents'][0]['model'], 'gpt-6.1-astra')
        self.assertEqual(plan['agents'][0]['model_selection']['policy_model'], 'gpt-6-astra')

    def test_each_catalog_is_resolved_without_mutating_or_caching_policy(self):
        original = copy.deepcopy(self.policy)
        self.assertEqual(self.plan(self.catalog + [self.release('gpt-6.1-sol')])['model'], 'gpt-6.1-sol')
        self.assertEqual(self.plan()['model'], 'gpt-6-sol')
        self.assertEqual(self.policy, original)
