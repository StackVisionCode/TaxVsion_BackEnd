import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Meeting } from '../../src/domain/meetings/meeting.js';
import type { MeetingInvitation } from '../../src/domain/meetings/meeting-invitation.js';
import type { IntegrationEvent } from '../../src/contracts/events/integration-event.js';
import type { IncomingEnvelope } from '../../src/application/ports/event-consumer.js';
import { bindCalendarConsumers } from '../../src/application/event-handlers/calendar-consumers.js';

// B2 (Calendar): los asistentes de la cita quedan invitados a su sala; el cliente la ve en su portal.
describe('calendar consumers — invitados de la sala', () => {
  it('invita al personal y al cliente de la cita; no al organizador; un externo sin permiso no corta nada', async () => {
    const tenantId = randomUUID();
    const organizer = randomUUID();
    const staff = randomUUID();
    const customerId = randomUUID();
    const portalUser = randomUUID();
    const meetings = new Map<string, Meeting>();
    const invitations: MeetingInvitation[] = [];
    const events: IntegrationEvent[] = [];
    const handlers = new Map<string, (env: IncomingEnvelope) => Promise<void>>();

    bindCalendarConsumers((type, handler) => handlers.set(type, handler), {
      meetings: {
        findById: async (_t: string, id: string) => meetings.get(id) ?? null,
        findByAppointmentId: async () => null,
        save: async (m: Meeting) => void meetings.set(m.id, m),
        listInvitationsByMeeting: async () => [...invitations],
        saveInvitation: async (i: MeetingInvitation) => void invitations.push(i),
      },
      appointmentRoomLock: { run: (_t: string, _a: string, work: () => Promise<unknown>) => work() },
      publisher: { enqueue: async (e: IntegrationEvent) => void events.push(e) },
      passcodes: { hash: async () => 'h' },
      settings: { get: async () => ({ chatEnabled: true }) },
      meetingMediaPolicy: { get: async () => ({ meshMaxParticipants: 4, defaultMaxParticipants: 10 }) },
      effectiveSettings: {
        resolve: async () => ({
          meeting: { defaultDurationMinutes: 60, maxDurationMinutes: 240, guestsAllowed: false },
          invitations: { minTtlHours: 168, expiryGraceHours: 24 },
        }),
      },
      customerDirectory: {
        findActiveByEmail: async (_t: string, email: string) =>
          email === 'client@example.com' ? [{ customerId, email, displayName: 'Ana Client' }] : [],
      },
      userDirectory: { findActiveStaffByEmail: async () => null, findByUserId: async () => null },
      customerPortalAccounts: {
        findActiveByCustomerId: async (id: string) => (id === customerId ? { customerId, tenantId, userId: portalUser, isActive: true } : null),
      },
      tenantHostResolver: { resolveHost: async () => 'office.example.com', resolveTimeZone: async () => 'America/New_York' },
      maxInvitationsPerMeeting: 200,
    } as never);

    await handlers.get('calendar.appointment_scheduled.v1')!({
      eventId: randomUUID(),
      eventType: 'calendar.appointment_scheduled.v1',
      tenantId,
      occurredOnUtc: new Date().toISOString(),
      payload: {
        TenantId: tenantId,
        AppointmentId: randomUUID(),
        Title: 'Return review',
        OrganizerUserId: organizer,
        StartUtc: new Date(Date.now() + 86_400_000).toISOString(),
        IsVirtual: true,
        CustomerId: customerId,
        AttendeeUserIds: [staff],
        Recipients: [
          { Email: 'host@example.com', UserId: organizer },
          { Email: 'staff@example.com', UserId: staff },
          { Email: 'client@example.com', UserId: null },
          { Email: 'outsider@example.com', UserId: null },
        ],
      },
    });

    expect(meetings.size).toBe(1);
    const invited = invitations.map((i) => i.toSnapshot());
    expect(invited.map((i) => i.inviteeUserId).sort()).toEqual([portalUser, staff].sort());
    expect(invited.find((i) => i.inviteeUserId === portalUser)?.inviteeKind).toBe('Customer');
    expect(events.some((e) => e.eventType === 'communication.meeting.linked_to_appointment.v1')).toBe(true);
  });

  it('quien se suma después a una cita virtual queda invitado a su sala; a una presencial, no', async () => {
    const tenantId = randomUUID();
    const organizer = randomUUID();
    const andres = randomUUID();
    const appointmentId = randomUUID();
    const scheduled = Meeting.schedule({
      tenantId,
      title: 'Consulta inicial',
      host: { userId: organizer, displayName: 'Carlos' },
      scheduledForUtc: new Date(Date.now() + 86_400_000),
      appointmentId,
    });
    if (!scheduled.isSuccess) throw new Error('schedule failed');
    const room = scheduled.value;
    const invitations: MeetingInvitation[] = [];
    const handlers = new Map<string, (env: IncomingEnvelope) => Promise<void>>();

    bindCalendarConsumers((type, handler) => handlers.set(type, handler), {
      meetings: {
        findById: async () => room,
        findByAppointmentId: async (_t: string, id: string) => (id === appointmentId ? room : null),
        save: async () => {},
        listInvitationsByMeeting: async () => [...invitations],
        saveInvitation: async (i: MeetingInvitation) => void invitations.push(i),
      },
      appointmentRoomLock: { run: (_t: string, _a: string, work: () => Promise<unknown>) => work() },
      publisher: { enqueue: async () => {} },
      passcodes: { hash: async () => 'h' },
      settings: { get: async () => ({ chatEnabled: true }) },
      meetingMediaPolicy: { get: async () => ({ meshMaxParticipants: 4, defaultMaxParticipants: 10 }) },
      effectiveSettings: {
        resolve: async () => ({
          meeting: { defaultDurationMinutes: 60, maxDurationMinutes: 240, guestsAllowed: true },
          invitations: { minTtlHours: 168, expiryGraceHours: 24 },
        }),
      },
      customerDirectory: { findActiveByEmail: async () => [] },
      userDirectory: { findActiveStaffByEmail: async () => null, findByUserId: async () => null },
      customerPortalAccounts: { findActiveByCustomerId: async () => null },
      tenantHostResolver: { resolveHost: async () => 'office.example.com', resolveTimeZone: async () => 'America/New_York' },
      maxInvitationsPerMeeting: 200,
    } as never);

    const added = (isVirtual: boolean) => ({
      eventId: randomUUID(),
      eventType: 'calendar.attendee_added.v1',
      tenantId,
      occurredOnUtc: new Date().toISOString(),
      payload: { TenantId: tenantId, AppointmentId: appointmentId, AttendeeKind: 'InternalUser', UserId: andres, Email: 'andres@example.com', IsVirtual: isVirtual },
    });

    await handlers.get('calendar.attendee_added.v1')!(added(false));
    expect(invitations).toHaveLength(0);

    await handlers.get('calendar.attendee_added.v1')!(added(true));
    expect(invitations.map((i) => i.toSnapshot().inviteeUserId)).toEqual([andres]);
  });

  it('el cliente de la cita entra a la sala como cliente aunque comparta correo con alguien del equipo', async () => {
    const tenantId = randomUUID();
    const organizer = randomUUID();
    const andres = randomUUID();
    const customerId = randomUUID();
    const portalUser = randomUUID();
    const meetings = new Map<string, Meeting>();
    const invitations: MeetingInvitation[] = [];
    const handlers = new Map<string, (env: IncomingEnvelope) => Promise<void>>();

    bindCalendarConsumers((type, handler) => handlers.set(type, handler), {
      meetings: {
        findById: async (_t: string, id: string) => meetings.get(id) ?? null,
        findByAppointmentId: async () => null,
        save: async (m: Meeting) => void meetings.set(m.id, m),
        listInvitationsByMeeting: async () => [...invitations],
        saveInvitation: async (i: MeetingInvitation) => void invitations.push(i),
      },
      appointmentRoomLock: { run: (_t: string, _a: string, work: () => Promise<unknown>) => work() },
      publisher: { enqueue: async () => {} },
      passcodes: { hash: async () => 'h' },
      settings: { get: async () => ({ chatEnabled: true }) },
      meetingMediaPolicy: { get: async () => ({ meshMaxParticipants: 4, defaultMaxParticipants: 10 }) },
      effectiveSettings: {
        resolve: async () => ({
          meeting: { defaultDurationMinutes: 60, maxDurationMinutes: 240, guestsAllowed: true },
          invitations: { minTtlHours: 168, expiryGraceHours: 24 },
        }),
      },
      // Con solo el correo, el compartido se resolvería como el empleado.
      customerDirectory: { findActiveByEmail: async () => [] },
      userDirectory: { findActiveStaffByEmail: async () => ({ userId: andres, email: 'shared@example.com', displayName: 'Andres' }), findByUserId: async () => null },
      customerPortalAccounts: {
        findActiveByCustomerId: async (id: string) => (id === customerId ? { customerId, tenantId, userId: portalUser, isActive: true } : null),
      },
      tenantHostResolver: { resolveHost: async () => 'office.example.com', resolveTimeZone: async () => 'America/New_York' },
      maxInvitationsPerMeeting: 200,
    } as never);

    await handlers.get('calendar.appointment_scheduled.v1')!({
      eventId: randomUUID(),
      eventType: 'calendar.appointment_scheduled.v1',
      tenantId,
      occurredOnUtc: new Date().toISOString(),
      payload: {
        TenantId: tenantId,
        AppointmentId: randomUUID(),
        Title: 'Consulta inicial',
        OrganizerUserId: organizer,
        StartUtc: new Date(Date.now() + 86_400_000).toISOString(),
        IsVirtual: true,
        Recipients: [
          { Email: 'shared@example.com', UserId: andres, CustomerId: null },
          { Email: 'shared@example.com', UserId: null, CustomerId: customerId },
        ],
      },
    });

    const invited = invitations.map((i) => i.toSnapshot());
    expect(invited.map((i) => i.inviteeUserId).sort()).toEqual([andres, portalUser].sort());
    expect(invited.find((i) => i.inviteeUserId === portalUser)?.inviteeKind).toBe('Customer');
  });

  it('la sala dura lo que la cita y cada invitación lleva el nombre y el enlace de respuesta del invitado', async () => {
    const tenantId = randomUUID();
    const meetings = new Map<string, Meeting>();
    const invitations: MeetingInvitation[] = [];
    const handlers = new Map<string, (env: IncomingEnvelope) => Promise<void>>();
    const published: Array<{ eventType: string; rsvpToken?: string | null }> = [];

    bindCalendarConsumers((type, handler) => handlers.set(type, handler), {
      meetings: {
        findById: async (_t: string, id: string) => meetings.get(id) ?? null,
        findByAppointmentId: async () => null,
        save: async (m: Meeting) => void meetings.set(m.id, m),
        listInvitationsByMeeting: async () => [...invitations],
        saveInvitation: async (i: MeetingInvitation) => void invitations.push(i),
      },
      appointmentRoomLock: { run: (_t: string, _a: string, work: () => Promise<unknown>) => work() },
      publisher: { enqueue: async (e: { eventType: string }) => void published.push(e) },
      passcodes: { hash: async () => 'h' },
      settings: { get: async () => ({ chatEnabled: true }) },
      meetingMediaPolicy: { get: async () => ({ meshMaxParticipants: 4, defaultMaxParticipants: 10 }) },
      effectiveSettings: {
        resolve: async () => ({
          meeting: { defaultDurationMinutes: 60, maxDurationMinutes: 240, guestsAllowed: true },
          invitations: { minTtlHours: 168, expiryGraceHours: 24 },
        }),
      },
      // Manuel es cliente: reconocerlo por correo no debe perder su enlace.
      customerDirectory: {
        findActiveByEmail: async () => [{ customerId: randomUUID(), email: 'manuel@example.com', displayName: 'Manuel Perez' }],
      },
      userDirectory: { findActiveStaffByEmail: async () => null, findByUserId: async () => null },
      customerPortalAccounts: { findActiveByCustomerId: async () => null },
      tenantHostResolver: { resolveHost: async () => 'office.example.com', resolveTimeZone: async () => 'America/New_York' },
      maxInvitationsPerMeeting: 200,
    } as never);

    const start = new Date(Date.now() + 86_400_000);
    await handlers.get('calendar.appointment_scheduled.v1')!({
      eventId: randomUUID(),
      eventType: 'calendar.appointment_scheduled.v1',
      tenantId,
      occurredOnUtc: new Date().toISOString(),
      payload: {
        TenantId: tenantId,
        AppointmentId: randomUUID(),
        Title: 'Consulta inicial',
        OrganizerUserId: randomUUID(),
        StartUtc: start.toISOString(),
        EndUtc: new Date(start.getTime() + 30 * 60_000).toISOString(),
        IsVirtual: true,
        Recipients: [
          { Email: 'manuel@example.com', UserId: null, CustomerId: null, DisplayName: 'Manuel Perez', RsvpToken: 'tok-manuel' },
        ],
      },
    });

    const room = [...meetings.values()][0]!.toSnapshot();
    expect(room.scheduledDurationMinutes).toBe(30);
    expect(invitations.map((i) => i.toSnapshot().inviteeName)).toEqual(['Manuel Perez']);
    // El enlace viaja en el evento hacia el correo; no se guarda en la invitación.
    const created = published.filter((e) => e.eventType === 'communication.meeting.invitation_created.v1');
    expect(created.map((e) => e.rsvpToken)).toEqual(['tok-manuel']);
    expect(JSON.stringify(invitations[0]!.toSnapshot())).not.toContain('tok-manuel');
  });


  it('la sala de la cita toma el nombre nuevo cuando cambian los detalles', async () => {
    const tenantId = randomUUID();
    const meetings = new Map<string, Meeting>();
    const handlers = new Map<string, (env: IncomingEnvelope) => Promise<void>>();
    bindCalendarConsumers((type, handler) => handlers.set(type, handler), {
      meetings: {
        findById: async (_t: string, id: string) => meetings.get(id) ?? null,
        findByAppointmentId: async () => null,
        save: async (m: Meeting) => void meetings.set(m.id, m),
        listInvitationsByMeeting: async () => [],
        saveInvitation: async () => {},
      },
      appointmentRoomLock: { run: (_t: string, _a: string, work: () => Promise<unknown>) => work() },
      publisher: { enqueue: async () => {} },
      passcodes: { hash: async () => 'h' },
      settings: { get: async () => ({ chatEnabled: true }) },
      meetingMediaPolicy: { get: async () => ({ meshMaxParticipants: 4, defaultMaxParticipants: 10 }) },
      effectiveSettings: {
        resolve: async () => ({
          meeting: { defaultDurationMinutes: 60, maxDurationMinutes: 240, guestsAllowed: true },
          invitations: { minTtlHours: 168, expiryGraceHours: 24 },
        }),
      },
      customerDirectory: { findActiveByEmail: async () => [] },
      userDirectory: { findActiveStaffByEmail: async () => null, findByUserId: async () => null },
      customerPortalAccounts: { findActiveByCustomerId: async () => null },
      tenantHostResolver: { resolveHost: async () => 'office.example.com' },
      maxInvitationsPerMeeting: 200,
    } as never);

    const start = new Date(Date.now() + 86_400_000);
    const appointmentId = randomUUID();
    await handlers.get('calendar.appointment_scheduled.v1')!({
      eventId: randomUUID(),
      eventType: 'calendar.appointment_scheduled.v1',
      tenantId,
      occurredOnUtc: new Date().toISOString(),
      payload: {
        TenantId: tenantId,
        AppointmentId: appointmentId,
        Title: 'Consulta inicial',
        OrganizerUserId: randomUUID(),
        StartUtc: start.toISOString(),
        EndUtc: new Date(start.getTime() + 30 * 60_000).toISOString(),
        IsVirtual: true,
        Recipients: [],
      },
    });
    const room = [...meetings.values()][0]!;

    await handlers.get('calendar.appointment_details_changed.v1')!({
      eventId: randomUUID(),
      eventType: 'calendar.appointment_details_changed.v1',
      tenantId,
      occurredOnUtc: new Date().toISOString(),
      payload: { TenantId: tenantId, AppointmentId: appointmentId, MeetingId: room.id, Title: 'Revisión final', ChangedFields: ['Title'] },
    });

    expect(meetings.get(room.id)!.toSnapshot().title).toBe('Revisión final');
  });
});
