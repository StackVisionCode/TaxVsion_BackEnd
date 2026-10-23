import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.join(path.dirname(fileURLToPath(import.meta.url)), 'v1');
const emptyExpected = { valid: true, issues: [] };
const issue = (code, pointer) => ({ valid: false, issues: [{ code, path: pointer }] });
const richText = (text = 'Instructions') => ({
  type: 'doc',
  blocks: [{ type: 'paragraph', children: [{ type: 'text', text }] }],
});
const inputTypes = new Set([
  'text', 'textarea', 'number', 'currency', 'date', 'email', 'phone', 'select', 'radio',
  'checkboxGroup', 'yesNo', 'checkbox', 'file', 'address', 'taxId',
]);
const component = (type = 'text', props = {}, overrides = {}) => ({
  id: 'fd_abcdefgh',
  type,
  ...(inputTypes.has(type) ? { label: 'Answer' } : {}),
  props,
  ...overrides,
});
const definition = (components = [component()], overrides = {}) => ({
  schemaVersion: 1,
  title: 'Fixture form',
  purpose: 'clientIntake',
  presentation: 'wizard',
  pages: [{
    id: 'pg_abcdefgh',
    title: 'Page',
    sections: [{ id: 'sc_abcdefgh', title: 'Section', components }],
  }],
  ...overrides,
});
const answers = (values = {}) => ({ schemaVersion: 1, values });
const fixture = (name, mode, def, answerValues, expected = emptyExpected) => ({
  name,
  mode,
  definition: def,
  ...(answerValues === undefined ? {} : { answers: answers(answerValues) }),
  expected,
});
const options = [{ value: 'first', label: 'First' }, { value: 'second', label: 'Second' }];
const signAction = { key: 'sign', type: 'requestSignature', trigger: 'onSubmit' };
const repeaterDefinition = (minRows = 1, maxRows = 3) => definition([], {
  pages: [{
    id: 'pg_abcdefgh',
    title: 'Page',
    sections: [{
      id: 'sc_repeat000',
      title: 'Dependents',
      repeat: { minRows, maxRows, addLabel: 'Add row', rowLabel: 'Dependent' },
      components: [component('text', {}, { id: 'fd_rowname00', required: true })],
    }],
  }],
});

const toggle = component('yesNo', {}, { id: 'fd_toggle000', required: true, label: 'Continue?' });
const detail = component('text', {}, { id: 'fd_detail000', required: true, visibleWhen: { field: 'fd_toggle000', op: 'isTrue' }, label: 'Details' });
const conditionalDefinition = definition([toggle, detail]);
const guarded = (type, props, op, value) => definition([
  component(type, props, { id: 'fd_source000' }),
  component('text', {}, { id: 'fd_detail000', visibleWhen: { field: 'fd_source000', op, value } }),
]);

const structure = [
  fixture('valid text definition', 'structure', definition(), undefined),
  fixture('invalid page id', 'structure', definition(undefined, { pages: [{ id: 'bad', title: 'Page', sections: [{ id: 'sc_abcdefgh', title: 'Section', components: [component()] }] }] }), undefined, issue('id.invalid_format', '/pages/0/id')),
  fixture('duplicate component id', 'structure', definition([component(), component('text', {}, { label: 'Second' })]), undefined, issue('id.duplicate', '/pages/0/sections/0/components/1/id')),
  fixture('unknown component type', 'structure', definition([component('video')]), undefined, issue('component.unknown_type', '/pages/0/sections/0/components/0/type')),
  fixture('wrong property type', 'structure', definition([component('text', { maxLength: '200' })]), undefined, issue('props.out_of_range', '/pages/0/sections/0/components/0/props/maxLength')),
  fixture('duplicate option values', 'structure', definition([component('select', { options: [{ value: 'same', label: 'A' }, { value: 'same', label: 'B' }] })]), undefined, issue('options.duplicate_value', '/pages/0/sections/0/components/0/props/options/1/value')),
  fixture('radio needs two options', 'structure', definition([component('radio', { options: [options[0]] })]), undefined, issue('options.too_few', '/pages/0/sections/0/components/0/props/options')),
  fixture('required conflicts with requiredWhen', 'structure', definition([component('yesNo', {}, { id: 'fd_toggle000' }), component('text', {}, { id: 'fd_detail000', required: true, requiredWhen: { field: 'fd_toggle000', op: 'isTrue' } })]), undefined, issue('required.conflict', '/pages/0/sections/0/components/1')),
  fixture('reportable requires key', 'structure', definition([component('yesNo', {}, { reportable: true })]), undefined, issue('key.required', '/pages/0/sections/0/components/0/key')),
  fixture('file cannot be reportable', 'structure', definition([component('file', {}, { key: 'attachment', reportable: true })]), undefined, issue('reportable.not_allowed', '/pages/0/sections/0/components/0/reportable')),
  fixture('single page rejects two pages', 'structure', definition(undefined, { presentation: 'singlePage', pages: [definition().pages[0], { id: 'pg_ijklmnop', sections: [{ id: 'sc_ijklmnop', components: [component('text', {}, { id: 'fd_ijklmnop' })] }] }] }), undefined, issue('page.single_page_violation', '/pages')),
  fixture('repeater invalid bounds', 'structure', repeaterDefinition(4, 3), undefined, issue('repeater.invalid_bounds', '/pages/0/sections/0/repeat')),
  fixture('file extension not allowed', 'structure', definition([component('file', { allowedTypes: ['exe'] })]), undefined, issue('file.type_not_allowed', '/pages/0/sections/0/components/0/props/allowedTypes')),
  fixture('file size above platform', 'structure', definition([component('file', { maxFileBytes: 26214401 })]), undefined, issue('file.size_exceeds_platform', '/pages/0/sections/0/components/0/props/maxFileBytes')),
  fixture('rich text rejects http', 'structure', definition([component('paragraph', { text: { type: 'doc', blocks: [{ type: 'paragraph', children: [{ type: 'link', href: 'http://example.com', children: [{ type: 'text', text: 'Link' }] }] }] } })]), undefined, issue('richtext.invalid_link', '/pages/0/sections/0/components/0/props/text/blocks/0/children/0/href')),
  fixture('rich text rejects unknown node', 'structure', definition([component('paragraph', { text: { type: 'doc', blocks: [{ type: 'paragraph', children: [{ type: 'image' }, { type: 'text', text: 'Valid text' }] }] } })]), undefined, issue('richtext.invalid_node', '/pages/0/sections/0/components/0/props/text/blocks/0/children/0')),
  fixture('tax id data class cannot be lowered', 'structure', definition([component('taxId', {}, { dataClass: 'personal' })]), undefined, issue('dataclass.below_minimum', '/pages/0/sections/0/components/0/dataClass')),
  fixture('tax identifier words require taxId', 'structure', definition([component('text', {}, { label: 'Enter SSN' })]), undefined, issue('component.restricted_requires_taxid', '/pages/0/sections/0/components/0')),
  fixture('card collection is forbidden', 'structure', definition([component('text', {}, { label: 'Enter card number' })]), undefined, issue('component.unsupported_restricted_data', '/pages/0/sections/0/components/0')),
  fixture('signature step needs action', 'structure', definition([component('signatureStep', { documentSource: 'submissionSummary', verification: 'emailOtp' })]), undefined, issue('signature.action_missing', '/actions')),
  fixture('signature action needs step', 'structure', definition(undefined, { actions: [{ key: 'sign', type: 'requestSignature', trigger: 'onSubmit' }] }), undefined, issue('signature.action_missing', '/actions')),
  fixture('implicit action cannot be configured', 'structure', definition(undefined, { actions: [{ key: 'notify', type: 'notifyStaff', trigger: 'onSubmit' }] }), undefined, issue('props.out_of_range', '/actions/0/type')),
  fixture('action rejects unknown config', 'structure', definition(undefined, { actions: [{ key: 'create_client', type: 'createCustomer', trigger: 'onSubmit', config: { endpoint: 'https://example.com' } }] }), undefined, issue('props.unknown', '/actions/0/config/endpoint')),
  fixture('wizard page title required', 'structure', definition(undefined, { pages: [{ id: 'pg_abcdefgh', sections: [{ id: 'sc_abcdefgh', title: 'Section', components: [component()] }] }] }), undefined, issue('page.title_required', '/pages/0/title')),
  fixture('repeater title required', 'structure', definition(undefined, { pages: [{ id: 'pg_abcdefgh', title: 'Page', sections: [{ id: 'sc_repeat000', repeat: { minRows: 0, maxRows: 2, addLabel: 'Add', rowLabel: 'Row' }, components: [component()] }] }] }), undefined, issue('section.title_required', '/pages/0/sections/0/title')),
  fixture('unknown component prop', 'structure', definition([component('text', { customCss: 'red' })]), undefined, issue('props.unknown', '/pages/0/sections/0/components/0/props/customCss')),
  fixture('layout label not allowed', 'structure', definition([component('divider', {}, { label: 'No' })]), undefined, issue('component.label_not_allowed', '/pages/0/sections/0/components/0/label')),
  fixture('duplicate reportable key', 'structure', definition([component('text', {}, { key: 'shared_key' }), component('text', {}, { id: 'fd_ijklmnop', key: 'shared_key' })]), undefined, issue('key.duplicate', '/pages/0/sections/0/components/1/key')),
  fixture('valid style tokens', 'structure', definition([component('yesNo', {}, { style: { width: 'half', align: 'left', textSize: 'body', textColor: 'ink', background: 'surface', spacingTop: 4, spacingBottom: 4 } })]), undefined),
  fixture('invalid style token', 'structure', definition([component('yesNo', {}, { style: { width: '480px' } })]), undefined, issue('style.token_invalid', '/pages/0/sections/0/components/0/style/width')),
  fixture('invalid style contrast', 'structure', definition([component('yesNo', {}, { style: { textColor: 'brand', background: 'brandSubtle' } })]), undefined, issue('style.contrast_not_allowed', '/pages/0/sections/0/components/0/style')),
  fixture('valid mapping contract', 'structure', definition([component('yesNo', {}, { key: 'email_opt_in' })], { mappings: [{ componentId: 'fd_abcdefgh', targetKey: 'customer.email_enabled', transform: 'none' }] }), undefined),
  fixture('mapping source must exist', 'structure', definition(undefined, { mappings: [{ componentId: 'fd_unknown00', targetKey: 'customer.primary_email', transform: 'lowercase' }] }), undefined, issue('mapping.invalid_target', '/mappings/0/componentId')),
  fixture('signature instructions reject javascript link', 'structure', definition([component('signatureStep', { documentSource: 'submissionSummary', verification: 'emailOtp', instructions: { type: 'doc', blocks: [{ type: 'paragraph', children: [{ type: 'link', href: 'javascript:alert(1)', children: [{ type: 'text', text: 'Sign' }] }] }] } })], { actions: [signAction] }), undefined, issue('richtext.invalid_link', '/pages/0/sections/0/components/0/props/instructions/blocks/0/children/0/href')),
  fixture('signature instructions reject unknown block', 'structure', definition([component('signatureStep', { documentSource: 'submissionSummary', verification: 'emailOtp', instructions: { type: 'doc', blocks: [{ type: 'table', children: [{ type: 'text', text: 'Cell' }] }] } })], { actions: [signAction] }), undefined, issue('richtext.invalid_node', '/pages/0/sections/0/components/0/props/instructions/blocks/0')),
  fixture('non-integer prop rejected', 'structure', definition([component('text', { maxLength: 2.5 })]), undefined, issue('props.out_of_range', '/pages/0/sections/0/components/0/props/maxLength')),
  fixture('integer prop above int range rejected', 'structure', definition([component('textarea', { rows: 1e10 })]), undefined, issue('props.out_of_range', '/pages/0/sections/0/components/0/props/rows')),
  fixture('id with trailing newline rejected', 'structure', definition(undefined, { pages: [{ id: 'pg_abcdefgh\n', title: 'Page', sections: [{ id: 'sc_abcdefgh', title: 'Section', components: [component()] }] }] }), undefined, issue('id.invalid_format', '/pages/0/id')),
];

const answerCases = [
  ['valid text', 'text', {}, 'Alice'],
  ['valid textarea', 'textarea', {}, 'Line one\nLine two'],
  ['valid number', 'number', { decimals: 2 }, '12.50'],
  ['valid currency', 'currency', { currency: 'USD' }, '25.00'],
  ['valid date', 'date', {}, '2026-10-20'],
  ['valid email', 'email', {}, 'person@example.com'],
  ['valid phone', 'phone', { defaultCountry: 'US' }, '+18095551234'],
  ['valid select', 'select', { options }, 'first'],
  ['valid radio', 'radio', { options }, 'second'],
  ['valid checkbox group', 'checkboxGroup', { options, minSelected: 1, maxSelected: 2 }, ['first']],
  ['valid yes no', 'yesNo', {}, true],
  ['valid checkbox', 'checkbox', { text: richText('I agree') }, true],
  ['valid file', 'file', { maxFiles: 2 }, [{ fileId: '00000000-0000-0000-0000-000000000001' }]],
  ['valid address', 'address', { country: 'US' }, { line1: '10 Main St', city: 'Albany', state: 'NY', postalCode: '12207', country: 'US' }],
  ['valid tax id reference', 'taxId', { kinds: ['ssn'] }, { sensitiveRef: '00000000-0000-0000-0000-000000000001', last4: '1234', kind: 'ssn' }],
].map(([name, type, props, value]) => fixture(name, 'submit', definition([component(type, props)]), { fd_abcdefgh: value }));

answerCases.push(
  fixture('number too many decimals', 'submit', definition([component('number', { decimals: 2 })]), { fd_abcdefgh: '1.234' }, issue('answer.too_many_decimals', '/values/fd_abcdefgh')),
  fixture('impossible date', 'submit', definition([component('date')]), { fd_abcdefgh: '2026-02-30' }, issue('answer.invalid_date', '/values/fd_abcdefgh')),
  fixture('invalid email', 'submit', definition([component('email')]), { fd_abcdefgh: 'invalid@example' }, issue('answer.invalid_email', '/values/fd_abcdefgh')),
  fixture('invalid phone', 'submit', definition([component('phone')]), { fd_abcdefgh: '555-1234' }, issue('answer.invalid_phone', '/values/fd_abcdefgh')),
  fixture('invalid option', 'submit', definition([component('select', { options })]), { fd_abcdefgh: 'third' }, issue('answer.invalid_option', '/values/fd_abcdefgh')),
  fixture('checkbox group exceeds max', 'submit', definition([component('checkboxGroup', { options, maxSelected: 1 })]), { fd_abcdefgh: ['first', 'second'] }, issue('answer.selection_count', '/values/fd_abcdefgh')),
  fixture('required unanswered on submit', 'submit', definition([component('text', {}, { required: true })]), {}, issue('answer.required', '/values/fd_abcdefgh')),
  fixture('required unanswered in draft', 'draft', definition([component('text', {}, { required: true })]), {}),
  fixture('required checkbox unchecked in draft', 'draft', definition([component('checkbox', { text: richText('I agree') }, { required: true })]), { fd_abcdefgh: false }),
  fixture('required checkbox must be true', 'submit', definition([component('checkbox', { text: richText('I agree') }, { required: true })]), { fd_abcdefgh: false }, issue('answer.must_be_checked', '/values/fd_abcdefgh')),
  fixture('too many files', 'submit', definition([component('file', { maxFiles: 1 })]), { fd_abcdefgh: [{ fileId: '00000000-0000-0000-0000-000000000001' }, { fileId: '00000000-0000-0000-0000-000000000002' }] }, issue('answer.too_many_files', '/values/fd_abcdefgh')),
  fixture('tax id requires server reference', 'submit', definition([component('taxId', { kinds: ['ssn'] })]), { fd_abcdefgh: { last4: '1234', kind: 'ssn' } }, issue('answer.sensitive_invalid', '/values/fd_abcdefgh')),
  fixture('partial address is valid draft', 'draft', definition([component('address', { country: 'US' })]), { fd_abcdefgh: { line1: '10 Main St', country: 'US' } }),
  fixture('partial address fails submit', 'submit', definition([component('address', { country: 'US' })]), { fd_abcdefgh: { line1: '10 Main St', country: 'US' } }, issue('answer.required', '/values/fd_abcdefgh')),
  fixture('unknown answer key', 'draft', definition(), { fd_unknown00: 'value' }, issue('answer.unknown_component', '/values/fd_unknown00')),
  fixture('text rejects tax identifier', 'submit', definition(), { fd_abcdefgh: '123-45-6789' }, issue('answer.looks_like_tax_identifier', '/values/fd_abcdefgh')),
  fixture('text rejects card number', 'submit', definition(), { fd_abcdefgh: '4111111111111111' }, issue('answer.looks_like_card_number', '/values/fd_abcdefgh')),
  fixture('duplicate repeater row id', 'submit', repeaterDefinition(1, 3), { sc_repeat000: { rows: [{ rowId: 'rw_abcdefghij', values: { fd_rowname00: 'A' } }, { rowId: 'rw_abcdefghij', values: { fd_rowname00: 'B' } }] } }, issue('answer.row_id_duplicate', '/values/sc_repeat000/rows/1/rowId')),
  fixture('repeater below min is valid draft', 'draft', repeaterDefinition(1, 3), { sc_repeat000: { rows: [] } }),
  fixture('repeater below min fails submit', 'submit', repeaterDefinition(1, 3), { sc_repeat000: { rows: [] } }, issue('answer.rows_below_min', '/values/sc_repeat000/rows')),
  fixture('required answer is missing', 'submit', definition([component('text', {}, { required: true })]), {}, issue('answer.required', '/values/fd_abcdefgh')),
  fixture('wrong answer type', 'submit', definition(), { fd_abcdefgh: 42 }, issue('answer.type_mismatch', '/values/fd_abcdefgh')),
  fixture('address rejects extra property', 'submit', definition([component('address', { country: 'US' })]), { fd_abcdefgh: { line1: '10 Main St', city: 'Albany', state: 'NY', postalCode: '12207', country: 'US', admin: true } }, issue('answer.type_mismatch', '/values/fd_abcdefgh')),
  fixture('file rejects extra property', 'submit', definition([component('file')]), { fd_abcdefgh: [{ fileId: '00000000-0000-0000-0000-000000000001', name: 'secret.pdf' }] }, issue('answer.type_mismatch', '/values/fd_abcdefgh')),
  fixture('text rejects nine digit tax identifier', 'submit', definition(), { fd_abcdefgh: 'id 123456789' }, issue('answer.looks_like_tax_identifier', '/values/fd_abcdefgh')),
  fixture('text rejects ein pattern', 'submit', definition(), { fd_abcdefgh: 'EIN 12-3456789' }, issue('answer.looks_like_tax_identifier', '/values/fd_abcdefgh')),
  fixture('scattered digits are not a tax identifier', 'submit', definition(), { fd_abcdefgh: 'Unit 12, floor 3, ref 123456' }),
  fixture('ten digit run is not a tax identifier', 'submit', definition(), { fd_abcdefgh: 'Order 1234567890' }),
  fixture('text rejects dashed card number', 'submit', definition(), { fd_abcdefgh: '4111-1111-1111-1111' }, issue('answer.looks_like_card_number', '/values/fd_abcdefgh')),
  fixture('text rejects card followed by other digits', 'submit', definition(), { fd_abcdefgh: '4111111111111111 5551234' }, issue('answer.looks_like_card_number', '/values/fd_abcdefgh')),
  fixture('phone with trailing newline rejected', 'submit', definition([component('phone')]), { fd_abcdefgh: '+18095551234\n' }, issue('answer.invalid_phone', '/values/fd_abcdefgh')),
  fixture('hidden value is still guarded in draft', 'draft', conditionalDefinition, { fd_toggle000: false, fd_detail000: '123-45-6789' }, issue('answer.looks_like_tax_identifier', '/values/fd_detail000')),
  fixture('hidden value type is checked in draft', 'draft', conditionalDefinition, { fd_toggle000: false, fd_detail000: 42 }, issue('answer.type_mismatch', '/values/fd_detail000')),
  fixture('hidden value is ignored on submit', 'submit', conditionalDefinition, { fd_toggle000: false, fd_detail000: '123-45-6789' }),
  fixture('rows above max are reported and not evaluated', 'draft', repeaterDefinition(0, 2), { sc_repeat000: { rows: [{ rowId: 'rw_aaaaaaaaaa', values: { fd_rowname00: 'A' } }, { rowId: 'rw_bbbbbbbbbb', values: { fd_rowname00: 'B' } }, { rowId: 'rw_cccccccccc', values: { fd_rowname00: 42 } }] } }, issue('answer.rows_above_max', '/values/sc_repeat000/rows')),
);

const conditions = [
  fixture('forward condition rejected', 'structure', definition([component('text', {}, { visibleWhen: { field: 'fd_later0000', op: 'isAnswered' } }), component('text', {}, { id: 'fd_later0000' })]), undefined, issue('condition.forward_reference', '/pages/0/sections/0/components/0/visibleWhen/field')),
  fixture('unknown condition field', 'structure', definition([component('text', {}, { visibleWhen: { field: 'fd_unknown00', op: 'isAnswered' } })]), undefined, issue('condition.unknown_field', '/pages/0/sections/0/components/0/visibleWhen/field')),
  fixture('operator not allowed', 'structure', definition([toggle, component('text', {}, { id: 'fd_detail000', visibleWhen: { field: 'fd_toggle000', op: 'gt', value: 1 } })]), undefined, issue('condition.op_not_allowed', '/pages/0/sections/0/components/1/visibleWhen/op')),
  fixture('condition value required', 'structure', definition([component('select', { options }, { id: 'fd_select000' }), component('text', {}, { id: 'fd_detail000', visibleWhen: { field: 'fd_select000', op: 'equals' } })]), undefined, issue('condition.value_invalid', '/pages/0/sections/0/components/1/visibleWhen/value')),
  fixture('condition value forbidden', 'structure', definition([toggle, component('text', {}, { id: 'fd_detail000', visibleWhen: { field: 'fd_toggle000', op: 'isTrue', value: true } })]), undefined, issue('condition.value_invalid', '/pages/0/sections/0/components/1/visibleWhen/value')),
  fixture('condition shape is closed', 'structure', definition([toggle, component('text', {}, { id: 'fd_detail000', visibleWhen: { all: [{ field: 'fd_toggle000', op: 'isTrue' }], op: 'isAnswered' } })]), undefined, issue('condition.value_invalid', '/pages/0/sections/0/components/1/visibleWhen')),
  fixture('rows count over platform limit', 'structure', definition([], { pages: [{ id: 'pg_abcdefgh', title: 'Page', sections: [repeaterDefinition(0, 3).pages[0].sections[0], { id: 'sc_later0000', title: 'Later', visibleWhen: { rows: 'sc_repeat000', op: 'countGte', value: 51 }, components: [component('text', {}, { id: 'fd_later0000' })] }] }] }), undefined, issue('condition.value_invalid', '/pages/0/sections/1/visibleWhen/value')),
  fixture('hidden conditional field', 'visibility', conditionalDefinition, { fd_toggle000: false, fd_detail000: 'kept in draft' }, { valid: true, issues: [], visible: ['pg_abcdefgh', 'sc_abcdefgh', 'fd_toggle000'] }),
  fixture('visible conditional field', 'visibility', conditionalDefinition, { fd_toggle000: true, fd_detail000: 'shown' }, { valid: true, issues: [], visible: ['pg_abcdefgh', 'sc_abcdefgh', 'fd_toggle000', 'fd_detail000'] }),
  fixture('in values must be field options', 'structure', guarded('select', { options }, 'in', ['first', 'third']), undefined, issue('condition.value_invalid', '/pages/0/sections/0/components/1/visibleWhen/value')),
  fixture('contains value must be a field option', 'structure', guarded('checkboxGroup', { options }, 'contains', 'third'), undefined, issue('condition.value_invalid', '/pages/0/sections/0/components/1/visibleWhen/value')),
  fixture('number comparison requires a number', 'structure', guarded('number', {}, 'gt', '10'), undefined, issue('condition.value_invalid', '/pages/0/sections/0/components/1/visibleWhen/value')),
  fixture('date comparison requires an iso date', 'structure', guarded('date', {}, 'gte', '10/20/2026'), undefined, issue('condition.value_invalid', '/pages/0/sections/0/components/1/visibleWhen/value')),
  fixture('text equality requires a string', 'structure', guarded('text', {}, 'equals', 5), undefined, issue('condition.value_invalid', '/pages/0/sections/0/components/1/visibleWhen/value')),
  fixture('typed condition values are valid', 'structure', definition([component('number', {}, { id: 'fd_source000' }), component('date', {}, { id: 'fd_when00000' }), component('select', { options }, { id: 'fd_choice000' }), component('text', {}, { id: 'fd_detail000', visibleWhen: { all: [{ field: 'fd_source000', op: 'gt', value: 10.5 }, { field: 'fd_when00000', op: 'gte', value: '2026-01-01' }, { field: 'fd_choice000', op: 'in', value: ['first'] }] } })]), undefined),
  fixture('date condition with numeric value is false', 'visibility', guarded('date', {}, 'gt', 20260101), { fd_source000: '2026-10-20' }, { valid: true, issues: [], visible: ['pg_abcdefgh', 'sc_abcdefgh', 'fd_source000'] }),
  fixture('notContains over non-string answers is false', 'visibility', guarded('checkboxGroup', { options }, 'notContains', 'first'), { fd_source000: [1, 2] }, { valid: false, issues: [{ code: 'answer.type_mismatch', path: '/values/fd_source000' }], visible: ['pg_abcdefgh', 'sc_abcdefgh', 'fd_source000'] }),
  fixture('thousands separator is not a number', 'visibility', guarded('number', {}, 'equals', 1000), { fd_source000: '1,000' }, { valid: false, issues: [{ code: 'answer.type_mismatch', path: '/values/fd_source000' }], visible: ['pg_abcdefgh', 'sc_abcdefgh', 'fd_source000'] }),
  fixture('numbers compare by value', 'visibility', guarded('currency', {}, 'equals', 1000.5), { fd_source000: '1000.50' }, { valid: true, issues: [], visible: ['pg_abcdefgh', 'sc_abcdefgh', 'fd_source000', 'fd_detail000'] }),
  fixture('rows count reveals section', 'visibility', definition([], { pages: [{ id: 'pg_abcdefgh', title: 'Page', sections: [repeaterDefinition(0, 3).pages[0].sections[0], { id: 'sc_later0000', title: 'Later', visibleWhen: { rows: 'sc_repeat000', op: 'countGte', value: 1 }, components: [component('text', {}, { id: 'fd_later0000' })] }] }] }), { sc_repeat000: { rows: [{ rowId: 'rw_abcdefghij', values: { fd_rowname00: 'A' } }] } }, { valid: true, issues: [], visible: ['pg_abcdefgh', 'sc_repeat000', 'fd_rowname00', 'sc_later0000', 'fd_later0000'] }),
];

const progress = [
  fixture('one required answered', 'progress', definition([component('text', {}, { required: true })]), { fd_abcdefgh: 'done' }, { valid: true, issues: [], visible: ['pg_abcdefgh', 'sc_abcdefgh', 'fd_abcdefgh'], progress: { required: 1, answered: 1 } }),
  fixture('one required unanswered', 'progress', definition([component('text', {}, { required: true })]), {}, { valid: true, issues: [], visible: ['pg_abcdefgh', 'sc_abcdefgh', 'fd_abcdefgh'], progress: { required: 1, answered: 0 } }),
  fixture('hidden required excluded', 'progress', conditionalDefinition, { fd_toggle000: false }, { valid: true, issues: [], visible: ['pg_abcdefgh', 'sc_abcdefgh', 'fd_toggle000'], progress: { required: 1, answered: 1 } }),
  fixture('visible required included', 'progress', conditionalDefinition, { fd_toggle000: true }, { valid: true, issues: [], visible: ['pg_abcdefgh', 'sc_abcdefgh', 'fd_toggle000', 'fd_detail000'], progress: { required: 2, answered: 1 } }),
];

const groups = { structure, answers: answerCases, conditions, progress };
for (const [name, cases] of Object.entries(groups)) {
  const directory = path.join(root, name);
  fs.mkdirSync(directory, { recursive: true });
  fs.writeFileSync(path.join(directory, 'cases.json'), `${JSON.stringify(cases, null, 2)}\n`, 'utf8');
}

const count = Object.values(groups).reduce((total, cases) => total + cases.length, 0);
if (count < 70) throw new Error(`Expected at least 70 fixtures, generated ${count}.`);
console.log(`Generated ${count} fixtures.`);
