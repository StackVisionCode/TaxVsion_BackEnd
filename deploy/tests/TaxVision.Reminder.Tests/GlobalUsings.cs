// El aggregate se llama `Reminder` y vive bajo el namespace `TaxVision.Reminder.*`. En C# el
// nombre simple `Reminder` resuelve primero al NAMESPACE (`TaxVision.Reminder`), porque los
// miembros de namespace ganan sobre los tipos importados por `using`. Sin alias, cada firma tendría
// que escribirse `TaxVision.Reminder.Domain.Reminders.Reminder` — que es exactamente lo que le pasó
// a Tenant (ver `ITenantRepository`, con `Task<TaxVision.Tenant.Domain.Tenant?>`).
//
// El alias NO puede llamarse `Reminder`: seguiría perdiendo contra el namespace. `ReminderAggregate`
// no colisiona con nada y además dice qué es.
global using Xunit;
global using ReminderAggregate = TaxVision.Reminder.Domain.Reminders.Reminder;
