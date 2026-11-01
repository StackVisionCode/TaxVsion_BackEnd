import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import type { Call, CallSnapshot } from '../../src/domain/calls/call.js';
import type { CallRepository } from '../../src/application/ports/call-repository.js';
import type { IdempotencyStore, IdempotencyReservation } from '../../src/application/ports/idempotency-store.js';
import type { TenantSettingsProvider } from '../../src/application/ports/tenant-settings-provider.js';
import type { TenantCommunicationSettingsSnapshot } from '../../src/application/ports/tenant-settings-provider.js';
import { initiateCall } from '../../src/application/use-cases/initiate-call.js';
import { respondToCall } from '../../src/application/use-cases/respond-to-call.js';
import { FakeEffectiveSettingsResolver } from '../helpers/fake-effective-settings.js';
import { RecordingPublisher } from '../helpers/fake-meeting-runtime.js';

const u = (): string => randomUUID();

class Calls {
  readonly saved = new Map<string, Call>();
  open = 0;
  async save(call: Call): Promise<void> {
    this.saved.set(call.toSnapshot().id, call);
  }
  async findById(_t: string, id: string): Promise<Call | null> {
    return this.saved.get(id) ?? null;
  }
  async countOpenForTenant(): Promise<number> {
    return this.open;
  }
  async listRecentForCustomer(): Promise<CallSnapshot[]> {
    return [];
  }
  asRepository(): CallRepository {
    return this as unknown as CallRepository;
  }
}

const idempotency: IdempotencyStore = {
  async tryReserve<T>(): Promise<IdempotencyReservation<T>> {
    return { status: 'fresh', token: u() };
  },
  async commit(): Promise<void> {},
  async release(): Promise<void> {},
};

const settings: TenantSettingsProvider = {
  async get(tenantId: string): Promise<TenantCommunicationSettingsSnapshot> {
    return { tenantId, chatEnabled: true, employeeToEmployeeChatEnabled: true, restrictCustomerChatToAssignedPreparer: false, screenshotsEnabled: true };
  },
};

/** Resolver con limites de plan: callsEnabled/maxConcurrentCalls/maxMonthlyMinutes/maxCallMinutes. */
class PlanResolver extends FakeEffectiveSettingsResolver {
  constructor(private readonly plan: Partial<{ callsEnabled: boolean; maxConcurrentCalls: number; maxMonthlyMinutes: number; maxCallMinutes: number }>, private readonly videoCalls = true) {
    super();
  }
  override async resolve(tenantId: string) {
    const base = await super.resolve(tenantId);
    return {
      ...base,
      calls: {
        ...base.calls,
        enabled: this.plan.callsEnabled ?? true,
        videoEnabled: (this.plan.callsEnabled ?? true) && this.videoCalls,
        maxConcurrentCalls: this.plan.maxConcurrentCalls ?? null,
        maxMonthlyMinutes: this.plan.maxMonthlyMinutes ?? null,
        maxCallMinutes: this.plan.maxCallMinutes ?? null,
      },
    };
  }
}

function command(kind: 'Audio' | 'Video' = 'Audio') {
  return {
    tenantId: u(),
    correlationId: u(),
    clientKey: u(),
    kind,
    caller: { userId: u(), displayName: 'Caller', actorType: 'TenantEmployee' },
    callee: { userId: u(), displayName: 'Callee', actorType: 'CustomerPortal' },
  };
}

describe('call limits at initiate (F4, INV-12 H-3)', () => {
  it('rejects when calls are disabled or video calls are disabled', async () => {
    const calls = new Calls();
    const off = await initiateCall(command(), { calls: calls.asRepository(), idempotency, publisher: new RecordingPublisher(), settings, effectiveSettings: new PlanResolver({ callsEnabled: false }) });
    expect(!off.isSuccess && off.error.code).toBe('Calls.Disabled');
    const noVideo = await initiateCall(command('Video'), { calls: calls.asRepository(), idempotency, publisher: new RecordingPublisher(), settings, effectiveSettings: new PlanResolver({}, false) });
    expect(!noVideo.isSuccess && noVideo.error.code).toBe('Calls.VideoDisabled');
  });

  it('rejects with Call.LimitReached when concurrent or monthly limits are hit, otherwise starts the call', async () => {
    const calls = new Calls();
    calls.open = 2;
    const concurrent = await initiateCall(command(), { calls: calls.asRepository(), idempotency, publisher: new RecordingPublisher(), settings, effectiveSettings: new PlanResolver({ maxConcurrentCalls: 2 }) });
    expect(!concurrent.isSuccess && concurrent.error).toMatchObject({ code: 'Call.LimitReached', message: 'concurrent' });

    calls.open = 0;
    const usage = { addSeconds: async () => undefined, getSeconds: async () => 600 * 60 };
    const monthly = await initiateCall(command(), { calls: calls.asRepository(), idempotency, publisher: new RecordingPublisher(), settings, effectiveSettings: new PlanResolver({ maxMonthlyMinutes: 600 }), callUsage: usage });
    expect(!monthly.isSuccess && monthly.error).toMatchObject({ code: 'Call.LimitReached', message: 'monthly' });

    const ok = await initiateCall(command(), { calls: calls.asRepository(), idempotency, publisher: new RecordingPublisher(), settings, effectiveSettings: new PlanResolver({ maxConcurrentCalls: 2, maxMonthlyMinutes: 600 }), callUsage: { ...usage, getSeconds: async () => 10 } });
    expect(ok.isSuccess).toBe(true);
  });

  it('fixes the plan cap on accept so the sweep can cut the call', async () => {
    const calls = new Calls();
    const cmd = command();
    const started = await initiateCall(cmd, { calls: calls.asRepository(), idempotency, publisher: new RecordingPublisher(), settings });
    if (!started.isSuccess) throw new Error();
    const accepted = await respondToCall(
      { tenantId: cmd.tenantId, correlationId: u(), clientKey: u(), callId: started.value.callId, actorUserId: cmd.callee.userId, actorDisplayName: 'Callee', actorType: 'CustomerPortal', action: 'accept' },
      { calls: calls.asRepository(), idempotency, publisher: new RecordingPublisher(), effectiveSettings: new PlanResolver({ maxCallMinutes: 45 }) },
    );
    expect(accepted.isSuccess).toBe(true);
    const snap = calls.saved.get(started.value.callId)!.toSnapshot();
    expect(snap.maxDurationMinutes).toBe(45);
    expect(snap.endsAtUtc).not.toBeNull();
    // Sin cuenta de portal inyectada, el callee queda sin CustomerId (nunca inventado).
    expect(snap.participants.find((p) => p.role === 'Callee')?.customerId).toBeNull();
  });
});
