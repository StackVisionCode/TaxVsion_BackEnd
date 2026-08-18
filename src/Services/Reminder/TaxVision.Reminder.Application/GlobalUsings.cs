// La clase `Reminder` pierde contra el namespace `TaxVision.Reminder` en la resolución de nombres
// de C# (medido: `error CS0118: 'Reminder' is a namespace but is used like a type`). El alias no
// puede llamarse `Reminder` — seguiría perdiendo. Ver `01_Modelo_De_Dominio.md` §1.1.
global using ReminderAggregate = TaxVision.Reminder.Domain.Reminders.Reminder;
