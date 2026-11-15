import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';

// Guardrail de tiempo (P22): en SQL Server CURRENT_TIMESTAMP y GETDATE() son la hora local del servidor de base.
// Una migración nueva usa SYSUTCDATETIME(); las viejas se corrigen cuando se toque esa tabla.
const LOCAL_CLOCK = /CURRENT_TIMESTAMP|GETDATE\s*\(|SYSDATETIME\s*\(/i;

// Migraciones anteriores a la regla (2026-11-15) que ya usan el reloj local en un default.
const LEGACY: readonly string[] = [
  '20260710133740_initial_migration',
  '20260712134324_add_user_directory_entry',
  '20260712140554_add_attachment_tracking',
  '20260716160158_add_recording_consent_and_session',
  '20260716183242_add_reactions_pin_forward',
  '20260716190100_add_customer_directory_and_actor_type',
  '20260720224947_add_role_permissions_projection',
  '20260720234529_add_customer_portal_account_and_notification_action_mapping',
  '20260721013500_add_customer_preparer_assignment',
  '20261029180000_add_communication_configuration_layer',
  '20261030011000_add_call_duration_limits_and_usage',
  '20261031010000_add_server_recording_transcript_artifacts',
  '20261031140000_group_roles_info_audit',
  '20261031230000_attachment_upload_reservation',
];

describe('Prisma migrations', () => {
  it('ninguna migración nueva pone un default con la hora local del servidor', () => {
    const dir = join(__dirname, '..', '..', 'prisma', 'migrations');
    const offenders = readdirSync(dir, { withFileTypes: true })
      .filter((entry) => entry.isDirectory() && !LEGACY.includes(entry.name))
      .filter((entry) => LOCAL_CLOCK.test(readFileSync(join(dir, entry.name, 'migration.sql'), 'utf8')))
      .map((entry) => entry.name);
    expect(offenders).toEqual([]);
  });
});
