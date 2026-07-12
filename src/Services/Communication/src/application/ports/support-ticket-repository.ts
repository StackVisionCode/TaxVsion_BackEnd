import type { SupportTicket, SupportTicketSnapshot } from '../../domain/support/support-ticket.js';

/**
 * Repositorio del aggregate SupportTicket. Es el UNICO repositorio del servicio
 * cuyos queries pueden atravesar el tenant filter — el resto siempre filtra por
 * TenantId. Aca, `findByIdForAgent` acepta al agente (aunque su AgentTenantId
 * sea distinto al TenantId del ticket) siempre que el use-case lo valide antes.
 */
export interface SupportTicketRepository {
  save(ticket: SupportTicket): Promise<void>;
  findById(id: string): Promise<SupportTicket | null>;
  listForCustomer(input: {
    tenantId: string;
    openedByUserId: string;
    take: number;
    skip: number;
    includeClosed?: boolean;
  }): Promise<SupportTicketSnapshot[]>;
  countForCustomer(tenantId: string, openedByUserId: string, includeClosed?: boolean): Promise<number>;
  listForAgentTenant(input: {
    agentTenantId: string;
    assignedAgentId?: string | null;
    take: number;
    skip: number;
    includeClosed?: boolean;
  }): Promise<SupportTicketSnapshot[]>;
  countForAgentTenant(agentTenantId: string, assignedAgentId?: string | null, includeClosed?: boolean): Promise<number>;
}
