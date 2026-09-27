import { describe, expect, it } from 'vitest';
import {
  checkPermission,
  configureModuleGate,
  CommunicationPermissions,
  type PermissionSubject,
} from '../../src/domain/shared/permissions.js';
import { shouldEnforceModule } from '../../src/domain/shared/module-gate-settings.js';
import { exemptPermissions, moduleFor } from '../../src/domain/shared/permission-module-map.js';
import type { UserPermissionsProjectionRepository } from '../../src/application/ports/user-permissions-projection-repository.js';

const TENANT_ID = '11111111-1111-1111-1111-111111111111';

function subject(userId: string): PermissionSubject {
  return { userId, tenantId: TENANT_ID, actorType: 'TenantEmployee', permissionVersion: 1 };
}

/** Proyeccion que concede exactamente los permisos que se le pasan. */
function projection(permissions: readonly string[]): UserPermissionsProjectionRepository {
  return {
    async findByUserId(userId: string) {
      return {
        userId,
        tenantId: TENANT_ID,
        permissions,
        permissionVersion: 1,
        roleIds: [],
        actorType: 'TenantEmployee',
        isActive: true,
        updatedAtUtc: new Date(),
      };
    },
  } as unknown as UserPermissionsProjectionRepository;
}

describe('A6/A5.2 — exenciones del gate de modulo', () => {
  it('los tres permisos exentos no pertenecen a ningun modulo', () => {
    expect(moduleFor('communication.notification.read')).toBeNull();
    expect(moduleFor('communication.support.open')).toBeNull();
    expect(moduleFor('communication.support.agent')).toBeNull();
  });

  it('las reuniones son su propio modulo', () => {
    // Se venden aparte (Pro y Enterprise); chat, llamadas y video van en todos los planes.
    expect(moduleFor('communication.meeting.create')).toBe('meetings');
    expect(moduleFor('communication.meeting.join')).toBe('meetings');
    expect(moduleFor('communication.meeting.host')).toBe('meetings');
    expect(moduleFor('communication.meeting.record')).toBe('meetings');
  });

  it('el prefijo mas especifico gana al general', () => {
    // `communication.meeting.` y `communication.` son el UNICO par que se solapa, y el mapa se evalua
    // en orden. Invertirlos no rompe la compilacion: manda las reuniones a `comms` en silencio y las
    // regala en Starter.
    expect(moduleFor('communication.meeting.create')).toBe('meetings');
    expect(moduleFor('communication.chat.start')).toBe('comms');
  });

  it('exentar no abre el resto del modulo comms', () => {
    // El bug que una exencion mal hecha produce: relajar el prefijo entero y regalar el chat.
    expect(moduleFor('communication.chat.start')).toBe('comms');
    expect(moduleFor('communication.chat.reply')).toBe('comms');
    expect(moduleFor('communication.call.start')).toBe('comms');
    expect(moduleFor('communication.settings.manage')).toBe('comms');
  });

  it('la lista es exactamente la del lado .NET', () => {
    // Espejo de `PermissionModuleMap.Exempt` (BuildingBlocks.Authorization). Si divergen, un tenant
    // recibe 403 en Communication y 200 en el resto, o al contrario. El lado .NET tiene su propio
    // test leyendo ESTE fichero, para que el guardarrail funcione en las dos direcciones.
    expect([...exemptPermissions].sort()).toEqual([
      'communication.notification.read',
      'communication.support.agent',
      'communication.support.open',
    ]);
  });
});

describe('A6 — camino de enforce del gate de modulo', () => {
  it('con el gate denegando, un permiso concedido termina en Authz.ModuleUnavailable', async () => {
    configureModuleGate(async () => ({ denied: true, module: 'comms' }));
    try {
      const result = await checkPermission(
        subject('u1'),
        CommunicationPermissions.ChatReply,
        projection([CommunicationPermissions.ChatReply]),
      );

      expect(result.allowed).toBe(false);
      if (result.allowed) return;
      expect(result.code).toBe('Authz.ModuleUnavailable');
      expect(result.message).toContain('comms');
    } finally {
      configureModuleGate(undefined);
    }
  });

  it('en log-only el gate no cambia la decision', async () => {
    // La garantia estructural: log-only devuelve `denied: false` y el permiso concedido sigue
    // concedido. Si esto se rompiera, encender la medicion denegaria de verdad.
    let observed = 0;
    configureModuleGate(async () => {
      observed++;
      return { denied: false };
    });
    try {
      const result = await checkPermission(
        subject('u2'),
        CommunicationPermissions.ChatReply,
        projection([CommunicationPermissions.ChatReply]),
      );

      expect(result.allowed).toBe(true);
      expect(observed).toBe(1);
    } finally {
      configureModuleGate(undefined);
    }
  });

  it('un fallo del gate NO deja a nadie fuera', async () => {
    // Una averia de lectura (Prisma caido, cache rota) no puede convertirse en perdida de acceso:
    // el permiso ya se concedio por la via normal. Misma direccion que el `null` del lector.
    configureModuleGate(async () => {
      throw new Error('prisma down');
    });
    try {
      const result = await checkPermission(
        subject('u3'),
        CommunicationPermissions.ChatReply,
        projection([CommunicationPermissions.ChatReply]),
      );

      expect(result.allowed).toBe(true);
    } finally {
      configureModuleGate(undefined);
    }
  });

  it('el escalon por modulo: con `comms` listado, `meetings` sigue en log-only', () => {
    // Este servicio emite permisos de DOS modulos. Con un solo booleano se encenderian los dos a la
    // vez y no se podria subir el escalon de uno en uno, que es justo lo que pide el plan.
    const gate = { enforce: true, enforcedModules: ['comms'] };

    expect(shouldEnforceModule(gate, 'comms')).toBe(true);
    expect(shouldEnforceModule(gate, 'meetings')).toBe(false);

    // Y el mapa manda cada permiso a su modulo, que es de donde sale ese `module`.
    expect(moduleFor('communication.chat.start')).toBe('comms');
    expect(moduleFor('communication.meeting.create')).toBe('meetings');
  });

  it('el interruptor general manda: apagado, listar modulos no enciende nada', () => {
    expect(shouldEnforceModule({ enforce: false, enforcedModules: ['comms'] }, 'comms')).toBe(false);
  });

  it('lista vacia = se aplican todos (estado final)', () => {
    const gate = { enforce: true, enforcedModules: [] };

    expect(shouldEnforceModule(gate, 'comms')).toBe(true);
    expect(shouldEnforceModule(gate, 'meetings')).toBe(true);
  });

  it('el gate no corre si el permiso ya fue denegado', async () => {
    // Orden del pipeline: modulo DESPUES de conceder el permiso, para que
    // `Authz.ModuleUnavailable` solo le aparezca a quien "podria si el plan lo tuviera" — es lo que
    // necesita la UX de upgrade, y evita filtrar que modulos tiene el plan a quien no tiene acceso.
    let ran = false;
    configureModuleGate(async () => {
      ran = true;
      return { denied: true, module: 'comms' };
    });
    try {
      const result = await checkPermission(
        subject('u4'),
        CommunicationPermissions.ChatReply,
        projection([]),
      );

      expect(result.allowed).toBe(false);
      if (result.allowed) return;
      expect(result.code).toBe('Auth.Forbidden');
      expect(ran).toBe(false);
    } finally {
      configureModuleGate(undefined);
    }
  });
});
