export const SupportCategory = {
  Billing: 'Billing',
  Technical: 'Technical',
  Account: 'Account',
  Other: 'Other',
} as const;
export type SupportCategory = (typeof SupportCategory)[keyof typeof SupportCategory];
export function isSupportCategory(value: string): value is SupportCategory {
  return value === 'Billing' || value === 'Technical' || value === 'Account' || value === 'Other';
}

export const SupportPriority = {
  Low: 'Low',
  Normal: 'Normal',
  High: 'High',
  Urgent: 'Urgent',
} as const;
export type SupportPriority = (typeof SupportPriority)[keyof typeof SupportPriority];
export function isSupportPriority(value: string): value is SupportPriority {
  return value === 'Low' || value === 'Normal' || value === 'High' || value === 'Urgent';
}

export const SupportStatus = {
  Open: 'Open',
  Claimed: 'Claimed',
  WaitingCustomer: 'WaitingCustomer',
  WaitingAgent: 'WaitingAgent',
  Resolved: 'Resolved',
  Closed: 'Closed',
} as const;
export type SupportStatus = (typeof SupportStatus)[keyof typeof SupportStatus];
export function isSupportStatus(value: string): value is SupportStatus {
  return (
    value === 'Open' ||
    value === 'Claimed' ||
    value === 'WaitingCustomer' ||
    value === 'WaitingAgent' ||
    value === 'Resolved' ||
    value === 'Closed'
  );
}

export function isTerminalSupport(status: SupportStatus): boolean {
  return status === 'Resolved' || status === 'Closed';
}
