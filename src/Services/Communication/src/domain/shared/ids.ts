import { z } from 'zod';

/**
 * Branded types para evitar confundir un TenantId con un UserId a nivel de tipos.
 * TypeScript los ve como el mismo string en runtime pero el compilador rechaza
 * el swap. Cierre CRIT-2 del legacy: sin bugs de case-sensitivity en GUIDs — el
 * parser exige lowercase, y siempre pasa por aqui antes de tocar dominio o socket.
 */

export type Brand<TValue, TBrand> = TValue & { readonly __brand: TBrand };

export type TenantId = Brand<string, 'TenantId'>;
export type UserId = Brand<string, 'UserId'>;
export type ConversationId = Brand<string, 'ConversationId'>;
export type MessageId = Brand<string, 'MessageId'>;
export type CallId = Brand<string, 'CallId'>;
export type MeetingId = Brand<string, 'MeetingId'>;
export type PeerId = Brand<string, 'PeerId'>;

const uuidLower = z
  .string()
  .regex(/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i, 'Invalid UUID');

function makeIdFactory<TBrand extends string>(_brand: TBrand): (raw: string) => Brand<string, TBrand> {
  return (raw: string) => uuidLower.parse(raw).toLowerCase() as Brand<string, TBrand>;
}

export const TenantId = makeIdFactory<'TenantId'>('TenantId');
export const UserId = makeIdFactory<'UserId'>('UserId');
export const ConversationId = makeIdFactory<'ConversationId'>('ConversationId');
export const MessageId = makeIdFactory<'MessageId'>('MessageId');
export const CallId = makeIdFactory<'CallId'>('CallId');
export const MeetingId = makeIdFactory<'MeetingId'>('MeetingId');
export const PeerId = makeIdFactory<'PeerId'>('PeerId');
