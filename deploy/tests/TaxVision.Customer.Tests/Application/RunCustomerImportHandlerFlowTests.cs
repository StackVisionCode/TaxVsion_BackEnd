using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Imports.Dtos;
using TaxVision.Customer.Application.Imports.Messages;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using TaxVision.Customer.Domain.FiscalProfiles;
using TaxVision.Customer.Domain.Imports;
using TaxVision.Customer.Domain.Relations;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Tests.Application;

/// <summary>
/// CUS-6: caracteriza el import de punta a punta por <see cref="RunCustomerImportHandler.Handle"/>, con dobles en
/// memoria: fases, resultado por fila, candados, cancelación y avisos. No ejercita SQL (eso lo cubren las T-INT).
/// </summary>
public sealed class RunCustomerImportHandlerFlowTests
{
    [Fact]
    public async Task A_missing_attempt_is_dropped()
    {
        var world = new ImportWorld();

        await world.RunAsync(Guid.NewGuid());

        Assert.Equal(0, world.Files.Downloads);
        Assert.Empty(world.Published);
    }

    [Fact]
    public async Task A_finished_attempt_is_not_run_again()
    {
        var world = new ImportWorld();
        world.Attempt.Start();
        world.Attempt.Fail("before");

        await world.RunAsync();

        Assert.Equal(0, world.Files.Downloads);
        Assert.Empty(world.Published);
    }

    [Fact]
    public async Task A_file_that_cannot_be_downloaded_fails_the_attempt_and_warns()
    {
        var world = new ImportWorld();
        world.Files.FailDownload = true;

        await world.RunAsync();

        Assert.Equal(ImportStatus.Failed, world.Attempt.Status);
        var failed = Assert.IsType<CustomerImportFailedIntegrationEvent>(Assert.Single(world.Published));
        Assert.Equal(world.Attempt.Id, failed.ImportJobId);
        Assert.Equal(world.Attempt.TenantId, failed.TenantId);
        Assert.Equal(world.Attempt.CreatedByUserId, failed.CreatedByUserId);
        Assert.Equal(world.Attempt.NotifyUserId, failed.NotifyUserId);
        Assert.Equal(world.Correlation.CorrelationId, failed.CorrelationId);
        Assert.Equal("Could not download the import file from CloudStorage: boom", failed.Reason);
    }

    [Fact]
    public async Task An_empty_file_fails_with_its_reason()
    {
        var world = new ImportWorld();

        await world.RunAsync();

        Assert.Equal(ImportStatus.Failed, world.Attempt.Status);
        Assert.Equal("File is empty.", world.Attempt.FailureReason);
        Assert.IsType<CustomerImportFailedIntegrationEvent>(Assert.Single(world.Published));
    }

    [Fact]
    public async Task A_file_over_the_limit_fails_with_its_reason()
    {
        var world = new ImportWorld(maxRows: 1);
        world.Rows.AddRange([ImportWorld.Person(2), ImportWorld.Person(3)]);

        await world.RunAsync();

        Assert.Equal(ImportStatus.Failed, world.Attempt.Status);
        Assert.Equal("File has 2 rows; maximum allowed is 1.", world.Attempt.FailureReason);
    }

    [Fact]
    public async Task New_rows_become_customers_without_a_notice_per_row()
    {
        var world = new ImportWorld();
        world.Rows.Add(ImportWorld.Person(2, taxIdentifier: "123-45-6789", withAddress: true, withSpouse: true));

        await world.RunAsync();

        Assert.Equal(ImportStatus.Completed, world.Attempt.Status);
        Assert.Equal(1, world.Attempt.TotalRows);
        Assert.Equal(1, world.Attempt.SuccessCount);
        var customer = Assert.Single(world.Customers.All);
        Assert.Equal(world.Attempt.TenantId, customer.TenantId);
        Assert.Equal("Row 2", customer.DisplayName);
        Assert.Empty(customer.DomainEvents);
        Assert.Single(customer.Addresses);
        Assert.Single(customer.Relations);
        Assert.NotNull(customer.FiscalProfile);
        Assert.Equal("6789", customer.FiscalProfile!.TaxIdentifierLast4);

        var done = world.Finished();
        Assert.Equal(world.Attempt.Id, done.ImportJobId);
        Assert.Equal(world.Attempt.CreatedByUserId, done.CreatedByUserId);
        Assert.Equal(world.Attempt.NotifyUserId, done.NotifyUserId);
        Assert.Equal(world.Attempt.CompletedAtUtc, done.CompletedAtUtc);
        Assert.Equal(world.Correlation.CorrelationId, done.CorrelationId);
        Assert.Equal(1, done.TotalRows);
        Assert.Equal(1, done.SuccessCount);
        Assert.Equal([customer.Id], done.CreatedCustomerIds);
        Assert.Empty(done.UpdatedCustomerIds);
        Assert.Equal(1, world.Files.Deletes);
    }

    [Fact]
    public async Task A_failing_row_keeps_its_reason_and_unknown_catalog_values_only_warn()
    {
        var world = new ImportWorld();
        world.Catalogs.Unknown = true;
        world.Rows.AddRange([
            new ImportCustomerRow
            {
                RowNumber = 2,
                Kind = "Alien",
                FirstName = "Bad",
                LastName = "Kind",
            },
            ImportWorld.Person(3, occupation: "Astronaut"),
            ImportWorld.Business(4, naics: "999999"),
            ImportWorld.Person(5),
        ]);

        await world.RunAsync();

        Assert.Equal(ImportStatus.Completed, world.Attempt.Status);
        Assert.Equal("Row.Kind", world.RowOf(2).ErrorCode);
        Assert.Equal("Bad Kind", world.RowOf(2).DisplayName);
        Assert.Equal(RowStatus.Success, world.RowOf(3).Status);
        Assert.Contains("Occupation 'Astronaut' is not in the catalog", world.RowOf(3).Warnings);
        Assert.Equal(RowStatus.Success, world.RowOf(4).Status);
        Assert.Contains("NAICS code '999999' is not in the catalog", world.RowOf(4).Warnings);
        Assert.Null(world.RowOf(5).Warnings);
        Assert.All(world.Customers.All, customer => Assert.Null(customer.OccupationId));
        Assert.Equal(2, world.Attempt.WarningCount);
        var done = world.Finished();
        Assert.Equal(1, done.FailedCount);
        Assert.Equal(3, done.SuccessCount);
    }

    [Fact]
    public async Task An_updated_row_keeps_the_warnings_of_its_optional_data()
    {
        var world = new ImportWorld(DuplicateStrategy.Overwrite);
        var existing = world.AddExisting();
        world.Detector.Match(2, existing, "email");
        world.Rows.Add(
            new ImportCustomerRow
            {
                RowNumber = 2,
                Kind = "Individual",
                FirstName = "Row",
                LastName = "2",
                Email = "row2@example.com",
                Phone = "12",
            }
        );

        await world.RunAsync();

        Assert.Equal(RowStatus.Updated, world.RowOf(2).Status);
        Assert.Contains("Phone", world.RowOf(2).Warnings);
        Assert.Null(existing.PrimaryPhone);
    }

    [Fact]
    public async Task A_duplicate_inside_the_chunk_fails_the_second_row()
    {
        var world = new ImportWorld();
        world.Rows.AddRange([ImportWorld.Person(2), ImportWorld.Person(3, email: "row2@example.com")]);

        await world.RunAsync();

        Assert.Equal(RowStatus.Success, world.RowOf(2).Status);
        Assert.Equal("Import.DuplicateInChunk", world.RowOf(3).ErrorCode);
        Assert.Single(world.Customers.All);
    }

    [Fact]
    public async Task Emails_are_locked_once_in_order_before_looking_for_duplicates()
    {
        var world = new ImportWorld();
        world.Rows.AddRange([
            ImportWorld.Person(2, email: "zed@example.com"),
            ImportWorld.Person(3, email: "amy@example.com"),
            ImportWorld.Person(4, email: "ZED@example.com"),
        ]);

        await world.RunAsync();

        Assert.Equal(
            [$"lock:import:{world.Attempt.Id:N}", "lock:amy@example.com", "lock:zed@example.com", "detect"],
            world.Steps
        );
    }

    // F-E: el candado del import cubre dos corridas del mismo import aunque las filas no traigan email; cada fila
    // sin email se bloquea con la misma llave que el alta (E-4).
    [Fact]
    public async Task Rows_without_email_are_locked_by_phone_and_name_after_the_import_lock()
    {
        var world = new ImportWorld();
        world.Rows.Add(ImportWorld.Person(2, email: "", phone: "+13055551234"));

        await world.RunAsync();

        Assert.Equal([$"lock:import:{world.Attempt.Id:N}", "lock:phone:+13055551234|row 2", "detect"], world.Steps);
    }

    // D-3: un Individual sin email entra si trae teléfono o fecha de nacimiento.
    [Fact]
    public async Task A_row_without_email_but_with_a_phone_creates_the_client()
    {
        var world = new ImportWorld();
        world.Rows.Add(ImportWorld.Person(2, email: "", phone: "+13055551234"));

        await world.RunAsync();

        Assert.Equal(RowStatus.Success, world.RowOf(2).Status);
        Assert.Null(Assert.Single(world.Customers.All).PrimaryEmail);
        Assert.Null(Assert.Single(Assert.Single(world.Chunks).Customers).PrimaryEmail);
    }

    [Fact]
    public async Task A_row_without_email_phone_or_date_of_birth_fails_with_the_contact_rule()
    {
        var world = new ImportWorld();
        world.Rows.Add(ImportWorld.Person(2, email: ""));

        await world.RunAsync();

        Assert.Equal("Customer.ContactRequired", world.RowOf(2).ErrorCode);
        Assert.Empty(world.Customers.All);
    }

    // Una empresa sin email entra si trae teléfono o EIN.
    [Theory]
    [InlineData("+13055551234", null)]
    [InlineData(null, "12-3456789")]
    public async Task A_business_row_without_email_is_created_with_a_phone_or_an_ein(string? phone, string? ein)
    {
        var world = new ImportWorld();
        world.Rows.Add(Company(phone, ein));

        await world.RunAsync();

        Assert.Equal(RowStatus.Success, world.RowOf(2).Status);
        var company = Assert.Single(world.Customers.All);
        Assert.Null(company.PrimaryEmail);
        Assert.Equal(ein is not null, company.FiscalProfile?.HasTaxIdentifier == true);
    }

    [Fact]
    public async Task A_business_row_without_email_phone_or_ein_fails()
    {
        var world = new ImportWorld();
        world.Rows.Add(Company(phone: null, ein: null));

        await world.RunAsync();

        Assert.Equal("Customer.ContactRequired", world.RowOf(2).ErrorCode);
        Assert.Empty(world.Customers.All);
    }

    private static ImportCustomerRow Company(string? phone, string? ein) =>
        new()
        {
            RowNumber = 2,
            Kind = "Business",
            LegalName = "Acme 2",
            BusinessStructure = "LLC",
            Phone = phone,
            TaxIdentifier = ein,
        };

    [Fact]
    public async Task Merge_adds_an_email_to_a_client_without_one()
    {
        var world = new ImportWorld(DuplicateStrategy.Merge);
        var existing = world.AddExisting(withEmail: false);
        world.Detector.Match(2, existing, "Phone+Name");
        world.Rows.Add(ImportWorld.Person(2, email: "found@example.com"));

        await world.RunAsync();

        Assert.Equal(RowStatus.Updated, world.RowOf(2).Status);
        Assert.Equal("found@example.com", existing.PrimaryEmail!.Value);
        Assert.Equal("found@example.com", Assert.Single(Assert.Single(world.Chunks).Customers).PrimaryEmail);
    }

    [Fact]
    public async Task Merge_does_not_take_the_email_of_another_client_and_warns()
    {
        var world = new ImportWorld(DuplicateStrategy.Merge);
        var existing = world.AddExisting(withEmail: false);
        world.Detector.Match(2, existing, "Phone+Name");
        world.Taken.EmailOwners["taken@example.com"] = Guid.NewGuid();
        world.Rows.Add(ImportWorld.Person(2, email: "taken@example.com"));

        await world.RunAsync();

        // Sin el email no queda nada nuevo: la fila se salta y conserva el aviso.
        Assert.Equal(RowStatus.Skipped, world.RowOf(2).Status);
        Assert.Null(existing.PrimaryEmail);
        Assert.Contains("already belongs to another client", world.RowOf(2).Warnings);
    }

    // E-1: una celda vacía nunca borra; el email tampoco.
    [Fact]
    public async Task Overwrite_without_email_keeps_the_existing_email()
    {
        var world = new ImportWorld(DuplicateStrategy.Overwrite);
        var existing = world.AddExisting();
        world.Detector.Match(2, existing, "Phone+Name");
        world.Rows.Add(ImportWorld.Person(2, email: "", phone: "+13055551234"));

        await world.RunAsync();

        Assert.Equal(RowStatus.Updated, world.RowOf(2).Status);
        Assert.Equal("old@example.com", existing.PrimaryEmail!.Value);
    }

    [Fact]
    public async Task Skip_strategy_leaves_the_existing_customer_alone()
    {
        var world = new ImportWorld(DuplicateStrategy.Skip);
        var existing = world.AddExisting();
        world.Rows.Add(ImportWorld.Person(2));
        world.Detector.Match(2, existing, "email");

        await world.RunAsync();

        Assert.Equal(RowStatus.Skipped, world.RowOf(2).Status);
        Assert.Equal("old@example.com", existing.PrimaryEmail.Value);
        var done = world.Finished();
        Assert.Equal(1, done.SkippedCount);
        Assert.Empty(done.UpdatedCustomerIds);
        Assert.Empty(world.Chunks);
    }

    [Fact]
    public async Task Overwrite_strategy_updates_the_existing_customer_without_a_notice_per_row()
    {
        var world = new ImportWorld(DuplicateStrategy.Overwrite);
        var existing = world.AddExisting();
        world.Rows.Add(ImportWorld.Person(2));
        world.Detector.Match(2, existing, "email");

        await world.RunAsync();

        Assert.Equal(RowStatus.Updated, world.RowOf(2).Status);
        Assert.Equal("row2@example.com", existing.PrimaryEmail.Value);
        Assert.Empty(existing.DomainEvents);
        var done = world.Finished();
        Assert.Equal(1, done.UpdatedCount);
        Assert.Equal([existing.Id], done.UpdatedCustomerIds);
    }

    [Fact]
    public async Task Merge_strategy_keeps_the_existing_email()
    {
        var world = new ImportWorld(DuplicateStrategy.Merge);
        var existing = world.AddExisting();
        world.Rows.Add(ImportWorld.Person(2));
        world.Detector.Match(2, existing, "email");

        await world.RunAsync();

        // Nada nuevo para el cliente: no cuenta como actualizado ni avisa a las proyecciones.
        Assert.Equal(RowStatus.Skipped, world.RowOf(2).Status);
        Assert.Equal("No changes: the client already has this information.", world.RowOf(2).Message);
        Assert.Equal("old@example.com", existing.PrimaryEmail.Value);
        Assert.Empty(world.Chunks);
    }

    [Fact]
    public async Task A_match_that_disappeared_fails_the_row()
    {
        var world = new ImportWorld(DuplicateStrategy.Overwrite);
        var ghost = world.AddExisting();
        world.Customers.All.Clear();
        world.Rows.Add(ImportWorld.Person(2));
        world.Detector.Match(2, ghost, "email");

        await world.RunAsync();

        Assert.Equal("Import.MatchedThenMissing", world.RowOf(2).ErrorCode);
    }

    [Fact]
    public async Task Rows_run_in_chunks_of_500_with_one_lookup_each()
    {
        var world = new ImportWorld();
        for (var row = 2; row < 503; row++)
            world.Rows.Add(ImportWorld.Person(row));

        await world.RunAsync();

        Assert.Equal(2, world.Steps.Count(step => step == "detect"));
        Assert.Equal(501, world.Attempt.SuccessCount);
    }

    [Fact]
    public async Task A_cancel_between_chunks_stops_and_confirms_with_what_was_done()
    {
        var world = new ImportWorld();
        for (var row = 2; row < 503; row++)
            world.Rows.Add(ImportWorld.Person(row));
        world.Detector.AfterFirstChunk = () => world.Attempt.RequestCancel(world.Attempt.CreatedByUserId);

        await world.RunAsync();

        Assert.Equal(ImportStatus.Canceled, world.Attempt.Status);
        var done = world.Finished();
        Assert.Equal(500, done.SuccessCount);
        Assert.Equal(501, done.TotalRows);
        Assert.Equal(1, world.Steps.Count(step => step == "detect"));
    }

    [Fact]
    public async Task A_file_that_cannot_be_deleted_does_not_undo_the_import()
    {
        var world = new ImportWorld();
        world.Files.FailDelete = true;
        world.Rows.Add(ImportWorld.Person(2));

        await world.RunAsync();

        Assert.Equal(ImportStatus.Completed, world.Attempt.Status);
        world.Finished();
    }

    [Fact]
    public async Task A_chunk_that_cannot_commit_fails_the_attempt()
    {
        var world = new ImportWorld();
        world.Rows.Add(ImportWorld.Person(2));
        world.Detector.Fail = true;

        await world.RunAsync();

        Assert.Equal(ImportStatus.Failed, world.Attempt.Status);
        Assert.Equal("detector down", world.Attempt.FailureReason);
        Assert.IsType<CustomerImportFailedIntegrationEvent>(Assert.Single(world.Published));
    }

    [Fact]
    public async Task A_host_shutdown_is_rethrown_without_failing_the_attempt()
    {
        var world = new ImportWorld();
        world.Rows.Add(ImportWorld.Person(2));
        using var cancelled = new CancellationTokenSource();
        world.Detector.BeforeLookup = cancelled.Cancel;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => world.RunAsync(ct: cancelled.Token));

        Assert.NotEqual(ImportStatus.Failed, world.Attempt.Status);
        Assert.Empty(world.Published);
    }

    // Reinicio del servicio con bloques ya guardados: el mensaje reentregado retoma y cierra con todos los ids.
    [Fact]
    public async Task A_redelivered_import_resumes_after_the_rows_already_applied()
    {
        var world = new ImportWorld();
        world.Rows.AddRange([ImportWorld.Person(2), ImportWorld.Person(3), ImportWorld.Person(4)]);
        world.Attempt.Start();
        world.Attempt.SetTotalRows(3);
        world.Attempt.MoveToApplying();
        var alreadyCreated = Guid.NewGuid();
        world.Attempt.RecordSuccess(2, alreadyCreated, "Row 2");

        await world.RunAsync();

        Assert.Equal(ImportStatus.Completed, world.Attempt.Status);
        Assert.Equal(3, world.Attempt.SuccessCount);
        Assert.Equal(2, world.Customers.All.Count);
        var done = world.Finished();
        Assert.Equal(3, done.SuccessCount);
        Assert.Equal(3, done.CreatedCustomerIds.Count);
        Assert.Contains(alreadyCreated, done.CreatedCustomerIds);
        // El bloque de antes del reinicio ya avisó: solo se anuncian las filas nuevas.
        var chunk = Assert.Single(world.Chunks);
        Assert.Equal(2, chunk.Customers.Count);
        Assert.DoesNotContain(chunk.Customers, entry => entry.CustomerId == alreadyCreated);
    }

    [Fact]
    public async Task A_cancel_interrupted_before_closing_is_confirmed_on_redelivery()
    {
        var world = new ImportWorld();
        world.Rows.Add(ImportWorld.Person(2));
        world.Attempt.Start();
        world.Attempt.MoveToApplying();
        world.Attempt.RequestCancel(world.Attempt.CreatedByUserId);

        await world.RunAsync();

        Assert.Equal(ImportStatus.Canceled, world.Attempt.Status);
        Assert.Empty(world.Customers.All);
        world.Finished();
    }

    // El índice único de SSN/EIN cuenta los archivados: chocar con uno tumbaba el bloque entero.
    [Fact]
    public async Task A_tax_id_held_by_an_archived_client_fails_only_that_row()
    {
        var world = new ImportWorld();
        world.Taken.TaxIdOwners["blind-123456789"] = Guid.NewGuid();
        world.Rows.AddRange([ImportWorld.Person(2, taxIdentifier: "123-45-6789"), ImportWorld.Person(3)]);

        await world.RunAsync();

        Assert.Equal(ImportStatus.Completed, world.Attempt.Status);
        Assert.Equal("Import.TaxIdentifierTaken", world.RowOf(2).ErrorCode);
        Assert.Equal(RowStatus.Success, world.RowOf(3).Status);
        Assert.Single(world.Customers.All);
    }

    [Fact]
    public async Task Updating_a_client_with_the_tax_id_of_another_client_fails_the_row()
    {
        var world = new ImportWorld(DuplicateStrategy.Overwrite);
        var existing = world.AddExisting();
        world.Detector.Match(2, existing, "Email");
        world.Taken.TaxIdOwners["blind-123456789"] = Guid.NewGuid();
        world.Rows.Add(ImportWorld.Person(2, email: "old@example.com", taxIdentifier: "123-45-6789"));

        await world.RunAsync();

        Assert.Equal("Import.TaxIdentifierTaken", world.RowOf(2).ErrorCode);
        Assert.Null(existing.FiscalProfile);
    }

    [Fact]
    public async Task Updating_a_client_with_its_own_tax_id_is_fine()
    {
        var world = new ImportWorld(DuplicateStrategy.Overwrite);
        var existing = world.AddExisting();
        world.Detector.Match(2, existing, "TaxIdentifier");
        world.Taken.TaxIdOwners["blind-123456789"] = existing.Id;
        world.Rows.Add(ImportWorld.Person(2, email: "old@example.com", taxIdentifier: "123-45-6789"));

        await world.RunAsync();

        Assert.Equal(RowStatus.Updated, world.RowOf(2).Status);
    }

    [Fact]
    public async Task A_spouse_tax_id_already_on_file_fails_the_row()
    {
        var world = new ImportWorld();
        world.Taken.SpouseTaxIdRelations["blind-987654321"] = Guid.NewGuid();
        world.Rows.Add(ImportWorld.Person(2, withSpouse: true));

        await world.RunAsync();

        Assert.Equal("Import.SpouseTaxIdentifierTaken", world.RowOf(2).ErrorCode);
        Assert.Empty(world.Customers.All);
    }

    [Fact]
    public async Task The_same_spouse_tax_id_twice_in_the_file_fails_the_second_row()
    {
        var world = new ImportWorld();
        world.Rows.AddRange([ImportWorld.Person(2, withSpouse: true), ImportWorld.Person(3, withSpouse: true)]);

        await world.RunAsync();

        Assert.Equal(RowStatus.Success, world.RowOf(2).Status);
        Assert.Equal("Import.DuplicateInChunk", world.RowOf(3).ErrorCode);
    }

    [Fact]
    public async Task A_new_client_keeps_its_spouse_tax_id()
    {
        var world = new ImportWorld();
        world.Rows.Add(ImportWorld.Person(2, withSpouse: true));

        await world.RunAsync();

        var spouse = Assert.Single(Assert.Single(world.Customers.All).Relations);
        Assert.Equal("4321", spouse.FiscalProfile?.TaxIdentifierLast4);
    }

    [Fact]
    public async Task Merge_adds_the_spouse_when_the_client_has_none()
    {
        var world = new ImportWorld(DuplicateStrategy.Merge);
        var existing = world.AddExisting();
        world.Detector.Match(2, existing, "email");
        world.Rows.Add(ImportWorld.Person(2, withSpouse: true));

        await world.RunAsync();

        Assert.Equal(RowStatus.Updated, world.RowOf(2).Status);
        var spouse = Assert.Single(existing.Relations);
        Assert.Equal("Sam", spouse.Name.FirstName);
        Assert.Equal("4321", spouse.FiscalProfile?.TaxIdentifierLast4);
    }

    [Fact]
    public async Task Merge_leaves_an_existing_spouse_alone()
    {
        var world = new ImportWorld(DuplicateStrategy.Merge);
        var existing = world.AddExisting();
        ImportWorld.AddSpouse(existing, "Pat");
        world.Detector.Match(2, existing, "email");
        world.Rows.Add(ImportWorld.Person(2, withSpouse: true));

        await world.RunAsync();

        var spouse = Assert.Single(existing.Relations);
        Assert.Equal("Pat", spouse.Name.FirstName);
        Assert.Null(spouse.FiscalProfile);
    }

    [Fact]
    public async Task Overwrite_updates_the_spouse_with_the_file()
    {
        var world = new ImportWorld(DuplicateStrategy.Overwrite);
        var existing = world.AddExisting();
        ImportWorld.AddSpouse(existing, "Pat");
        world.Detector.Match(2, existing, "email");
        world.Rows.Add(ImportWorld.Person(2, withSpouse: true));

        await world.RunAsync();

        Assert.Equal(RowStatus.Updated, world.RowOf(2).Status);
        var spouse = Assert.Single(existing.Relations);
        Assert.Equal("Sam", spouse.Name.FirstName);
        Assert.Equal("4321", spouse.FiscalProfile?.TaxIdentifierLast4);
    }

    [Fact]
    public async Task Merge_with_the_spouse_tax_id_of_another_client_fails_the_row()
    {
        var world = new ImportWorld(DuplicateStrategy.Merge);
        var existing = world.AddExisting();
        world.Detector.Match(2, existing, "email");
        world.Taken.SpouseTaxIdRelations["blind-987654321"] = Guid.NewGuid();
        world.Rows.Add(ImportWorld.Person(2, withSpouse: true));

        await world.RunAsync();

        Assert.Equal("Import.SpouseTaxIdentifierTaken", world.RowOf(2).ErrorCode);
        Assert.Empty(existing.Relations);
    }

    // D-5 (TCPA): sin consentimiento en el archivo, el SMS queda apagado; el email, activado.
    [Fact]
    public async Task New_clients_get_email_on_and_sms_off_by_default()
    {
        var world = new ImportWorld();
        world.Rows.Add(ImportWorld.Person(2));

        await world.RunAsync();

        var policy = Assert.Single(world.Customers.All).ContactPolicy;
        Assert.True(policy.EmailEnabled);
        Assert.False(policy.SmsEnabled);
        var card = Assert.Single(Assert.Single(world.Chunks).Customers);
        Assert.False(card.SmsEnabled);
    }

    [Fact]
    public async Task A_new_client_follows_the_opt_in_columns()
    {
        var world = new ImportWorld();
        world.Rows.Add(ImportWorld.Person(2, emailOptIn: "no", smsOptIn: "yes"));

        await world.RunAsync();

        var policy = Assert.Single(world.Customers.All).ContactPolicy;
        Assert.False(policy.EmailEnabled);
        Assert.True(policy.SmsEnabled);
    }

    [Fact]
    public async Task Overwrite_applies_only_the_opt_in_columns_that_have_a_value()
    {
        var world = new ImportWorld(DuplicateStrategy.Overwrite);
        var existing = world.AddExisting();
        world.Detector.Match(2, existing, "email");
        world.Rows.Add(ImportWorld.Person(2, smsOptIn: "no"));

        await world.RunAsync();

        Assert.True(existing.ContactPolicy.EmailEnabled);
        Assert.False(existing.ContactPolicy.SmsEnabled);
    }

    [Fact]
    public async Task Merge_never_changes_the_contact_permissions()
    {
        var world = new ImportWorld(DuplicateStrategy.Merge);
        var existing = world.AddExisting();
        world.Detector.Match(2, existing, "email");
        world.Rows.Add(ImportWorld.Person(2, emailOptIn: "no", smsOptIn: "no"));

        await world.RunAsync();

        Assert.True(existing.ContactPolicy.EmailEnabled);
        Assert.True(existing.ContactPolicy.SmsEnabled);
    }

    // D-4: filing status y AGI se guardan aunque la fila no traiga SSN; el SSN queda pendiente.
    [Fact]
    public async Task Fiscal_data_without_a_tax_id_is_kept_with_the_tax_id_pending()
    {
        var world = new ImportWorld();
        world.Rows.Add(ImportWorld.Person(2, filingStatus: "MFJ", priorYearAgi: "52000"));

        await world.RunAsync();

        var profile = Assert.Single(world.Customers.All).FiscalProfile!;
        Assert.False(profile.HasTaxIdentifier);
        Assert.Equal(FilingStatus.MarriedJoint, profile.FilingStatus);
        Assert.Equal(52_000m, profile.PriorYearAgi);
    }

    [Fact]
    public async Task A_row_without_fiscal_data_creates_no_fiscal_profile()
    {
        var world = new ImportWorld();
        world.Rows.Add(ImportWorld.Person(2));

        await world.RunAsync();

        Assert.Null(Assert.Single(world.Customers.All).FiscalProfile);
    }

    [Fact]
    public async Task Merge_completes_a_pending_tax_id_and_keeps_the_filing_status()
    {
        var world = new ImportWorld(DuplicateStrategy.Merge);
        var existing = world.AddExisting();
        existing.SetFiscalProfileWithoutTaxIdentifier(
            FiscalSubjectKind.Individual,
            FilingStatus.Single,
            null,
            false,
            Guid.NewGuid()
        );
        world.Detector.Match(2, existing, "email");
        world.Rows.Add(ImportWorld.Person(2, taxIdentifier: "123-45-6789", filingStatus: "MarriedJoint"));

        await world.RunAsync();

        Assert.Equal(RowStatus.Updated, world.RowOf(2).Status);
        Assert.Equal("6789", existing.FiscalProfile!.TaxIdentifierLast4);
        Assert.Equal(FilingStatus.Single, existing.FiscalProfile.FilingStatus);
    }

    [Fact]
    public async Task Overwrite_without_a_tax_id_updates_the_fiscal_data_and_keeps_the_tax_id()
    {
        var world = new ImportWorld(DuplicateStrategy.Overwrite);
        var existing = world.AddExisting();
        existing.SetFiscalProfile(
            FiscalSubjectKind.Individual,
            [7, 7, 7],
            "blind-old",
            "1111",
            FilingStatus.Single,
            null,
            false,
            null,
            null,
            Guid.NewGuid()
        );
        world.Detector.Match(2, existing, "email");
        world.Rows.Add(ImportWorld.Person(2, filingStatus: "HOH"));

        await world.RunAsync();

        Assert.Equal("1111", existing.FiscalProfile!.TaxIdentifierLast4);
        Assert.Equal(FilingStatus.HeadOfHousehold, existing.FiscalProfile.FilingStatus);
    }

    // El email es único por oficina: Overwrite no puede dárselo a otro cliente.
    [Fact]
    public async Task Overwrite_to_the_email_of_another_client_fails_the_row()
    {
        var world = new ImportWorld(DuplicateStrategy.Overwrite);
        var existing = world.AddExisting();
        world.Detector.Match(2, existing, "Phone+Name");
        world.Taken.EmailOwners["taken@example.com"] = Guid.NewGuid();
        world.Rows.Add(ImportWorld.Person(2, email: "taken@example.com"));

        await world.RunAsync();

        Assert.Equal("Import.EmailTaken", world.RowOf(2).ErrorCode);
        Assert.Equal("old@example.com", existing.PrimaryEmail.Value);
    }

    // Cada bloque guardado avisa en su misma transacción, con la ficha mínima de directorio (sin datos fiscales).
    [Fact]
    public async Task Each_saved_chunk_announces_its_new_clients_with_their_directory_card()
    {
        var world = new ImportWorld();
        world.Rows.AddRange([ImportWorld.Person(2, taxIdentifier: "123-45-6789"), ImportWorld.Person(3)]);

        await world.RunAsync();

        var chunk = Assert.Single(world.Chunks);
        Assert.Equal(world.Attempt.Id, chunk.ImportJobId);
        Assert.Equal(world.Attempt.TenantId, chunk.TenantId);
        Assert.Equal(world.Attempt.CreatedByUserId, chunk.CreatedByUserId);
        Assert.Equal(world.Correlation.CorrelationId, chunk.CorrelationId);
        Assert.Equal(1, chunk.ChunkNumber);
        Assert.Equal(2, chunk.Customers.Count);
        var first = chunk.Customers[0];
        var customer = world.Customers.All.Single(c => c.Id == first.CustomerId);
        Assert.True(first.IsNew);
        Assert.Equal("Individual", first.Kind);
        Assert.Equal("Row 2", first.DisplayName);
        Assert.Equal("row2@example.com", first.PrimaryEmail);
        Assert.Equal("En", first.Language);
        Assert.Equal(customer.Status.ToString(), first.Status);
        Assert.Equal(customer.ContactPolicy.EmailEnabled, first.EmailEnabled);
        Assert.Equal(customer.ContactPolicy.SmsEnabled, first.SmsEnabled);
        Assert.IsType<CustomersImportChunkCommittedIntegrationEvent>(world.Published[0]);
        Assert.IsType<CustomersBulkImportedIntegrationEvent>(world.Published[^1]);
    }

    [Fact]
    public async Task Updated_clients_are_announced_and_failed_rows_are_not()
    {
        var world = new ImportWorld(DuplicateStrategy.Overwrite);
        var existing = world.AddExisting();
        world.Detector.Match(2, existing, "Email");
        world.Rows.AddRange([
            ImportWorld.Person(2),
            new ImportCustomerRow
            {
                RowNumber = 3,
                Kind = "Alien",
                FirstName = "Bad",
                LastName = "Kind",
            },
        ]);

        await world.RunAsync();

        var entry = Assert.Single(Assert.Single(world.Chunks).Customers);
        Assert.Equal(existing.Id, entry.CustomerId);
        Assert.False(entry.IsNew);
        Assert.Equal("row2@example.com", entry.PrimaryEmail);
    }

    [Fact]
    public async Task A_big_file_announces_one_event_per_chunk()
    {
        var world = new ImportWorld();
        for (var row = 2; row < 503; row++)
            world.Rows.Add(ImportWorld.Person(row));

        await world.RunAsync();

        Assert.Equal([500, 1], world.Chunks.Select(chunk => chunk.Customers.Count));
        Assert.Equal([1, 2], world.Chunks.Select(chunk => chunk.ChunkNumber));
    }

    // Si un bloque posterior falla, los clientes de los bloques ya guardados ya avisaron.
    [Fact]
    public async Task A_failing_chunk_does_not_hide_the_clients_of_earlier_chunks()
    {
        var world = new ImportWorld();
        for (var row = 2; row < 503; row++)
            world.Rows.Add(ImportWorld.Person(row));
        world.Detector.AfterFirstChunk = () => world.Detector.Fail = true;

        await world.RunAsync();

        Assert.Equal(ImportStatus.Failed, world.Attempt.Status);
        Assert.Equal(500, Assert.Single(world.Chunks).Customers.Count);
        Assert.IsType<CustomerImportFailedIntegrationEvent>(world.Published[^1]);
    }

    private sealed class ImportWorld
    {
        private readonly ServiceProvider _provider;

        public ImportWorld(DuplicateStrategy strategy = DuplicateStrategy.Skip, int maxRows = 10_000)
        {
            Attempt = CustomerImportAttempt
                .Create(Guid.NewGuid(), Guid.NewGuid(), "key", strategy, ImportSourceKind.Csv, "clients.csv")
                .Value;
            Customers = new CustomerCommandEventCountTests.AllCustomers([]);
            Detector = new ScriptedDetector(Steps);
            var services = new ServiceCollection();
            services.AddSingleton<ICustomerImportRepository>(new SingleAttempt(Attempt));
            services.AddSingleton<ICustomerRepository>(Customers);
            services.AddSingleton<ICustomerDuplicateDetector>(Detector);
            services.AddSingleton<ICustomerImportIdentifierRegistry>(Taken);
            services.AddSingleton<ICatalogResolver>(Catalogs);
            services.AddSingleton<ICustomerCreationLock>(new StepLock(Steps));
            services.AddSingleton<ISensitiveDataProtector>(new TestProtector());
            services.AddSingleton<ICustomerImportCloudStorageClient>(Files);
            services.AddSingleton<ICustomerImportReaderFactory>(new FixedRows(Rows));
            services.AddSingleton<ICorrelationContext>(Correlation);
            services.AddSingleton<IUnitOfWork>(new NoUnitOfWork());
            services.AddSingleton<IOfficeTimeZone>(new UtcOfficeZone());
            services.AddSingleton<IConfiguration>(
                new ConfigurationBuilder()
                    .AddInMemoryCollection(
                        new Dictionary<string, string?> { ["CustomerImport:MaxRows"] = maxRows.ToString() }
                    )
                    .Build()
            );
            services.AddSingleton<ITransactionalUnitRunner>(sp => new InlineRunner(sp, Published));
            _provider = services.BuildServiceProvider();
        }

        public CustomerImportAttempt Attempt { get; }
        public CustomerCommandEventCountTests.AllCustomers Customers { get; }
        public ScriptedDetector Detector { get; }
        public ScriptedCatalogs Catalogs { get; } = new();
        public TakenIdentifiers Taken { get; } = new();
        public ScriptedFiles Files { get; } = new();
        public List<ImportCustomerRow> Rows { get; } = [];
        public List<object> Published { get; } = [];
        public List<string> Steps { get; } = [];
        public TestCorrelation Correlation { get; } = new();

        public CustomerImportRow RowOf(int number) => Attempt.Rows.Single(row => row.RowNumber == number);

        public List<CustomersImportChunkCommittedIntegrationEvent> Chunks =>
            Published.OfType<CustomersImportChunkCommittedIntegrationEvent>().ToList();

        public CustomersBulkImportedIntegrationEvent Finished() =>
            Assert.Single(Published.OfType<CustomersBulkImportedIntegrationEvent>());

        public DomainCustomer AddExisting(bool withEmail = true)
        {
            var customer = DomainCustomer
                .Register(
                    Attempt.TenantId,
                    CustomerKind.Individual,
                    PersonalName.Create("Old", "Client").Value,
                    null,
                    withEmail ? EmailAddress.Create("old@example.com").Value : null,
                    withEmail ? null : PhoneNumber.Create("+13055550000").Value,
                    Language.En,
                    Guid.NewGuid()
                )
                .Value;
            customer.ClearDomainEvents();
            Customers.All.Add(customer);
            return customer;
        }

        public static void AddSpouse(DomainCustomer customer, string firstName) =>
            customer.AddRelation(
                RelationshipKind.Spouse,
                RelationPurpose.TaxHouseholdMember,
                PersonalName.Create(firstName, "Client").Value,
                null,
                null,
                null,
                null,
                Guid.NewGuid()
            );

        public Task RunAsync(Guid? attemptId = null, CancellationToken ct = default) =>
            RunCustomerImportHandler.Handle(
                new RunCustomerImportMessage(attemptId ?? Attempt.Id),
                _provider.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<RunCustomerImportMessage>.Instance,
                ct
            );

        public static ImportCustomerRow Person(
            int number,
            string? email = null,
            string? taxIdentifier = null,
            bool withAddress = false,
            bool withSpouse = false,
            string? occupation = null,
            string? emailOptIn = null,
            string? smsOptIn = null,
            string? filingStatus = null,
            string? priorYearAgi = null,
            string? phone = null
        ) =>
            new()
            {
                RowNumber = number,
                Kind = "Individual",
                FirstName = "Row",
                LastName = number.ToString(),
                Email = email ?? $"row{number}@example.com",
                OccupationName = occupation,
                TaxIdentifier = taxIdentifier,
                AddressLine1 = withAddress ? "1 Main St" : null,
                City = withAddress ? "Miami" : null,
                Region = withAddress ? "FL" : null,
                PostalCode = withAddress ? "33101" : null,
                CountryCode = withAddress ? "US" : null,
                SpouseFirstName = withSpouse ? "Sam" : null,
                SpouseLastName = withSpouse ? "Row" : null,
                SpouseTaxIdentifier = withSpouse ? "987-65-4321" : null,
                EmailOptIn = emailOptIn,
                SmsOptIn = smsOptIn,
                Phone = phone,
                FilingStatus = filingStatus,
                PriorYearAgi = priorYearAgi,
            };

        public static ImportCustomerRow Business(int number, string naics) =>
            new()
            {
                RowNumber = number,
                Kind = "Business",
                LegalName = $"Acme {number}",
                BusinessStructure = "LLC",
                PrincipalBusinessActivityCode = naics,
                Email = $"acme{number}@example.com",
            };
    }

    private sealed class SingleAttempt(CustomerImportAttempt attempt) : ICustomerImportRepository
    {
        public Task<CustomerImportAttempt?> GetByIdAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(id == attempt.Id ? attempt : null);

        public Task<CustomerImportAttempt?> GetByIdempotencyKeyAsync(
            Guid tenantId,
            string idempotencyKey,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task<int> CountActiveByTenantAsync(Guid tenantId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task AddAsync(CustomerImportAttempt attempt, CancellationToken ct) => throw new NotSupportedException();

        public Task<IReadOnlyList<CustomerImportAttempt>> ListInFlightNotifyingAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct
        ) => throw new NotSupportedException();
    }

    private sealed class InlineRunner(IServiceProvider services, List<object> published) : ITransactionalUnitRunner
    {
        public async Task RunAsync(Func<TransactionalUnit, CancellationToken, Task<bool>> work, CancellationToken ct)
        {
            var pending = new List<object>();
            var saved = await work(
                new TransactionalUnit(
                    services,
                    message =>
                    {
                        pending.Add(message);
                        return ValueTask.CompletedTask;
                    }
                ),
                ct
            );
            if (saved)
                published.AddRange(pending);
        }
    }

    private sealed class ScriptedDetector(List<string> steps) : ICustomerDuplicateDetector
    {
        private readonly List<DuplicateMatch> _matches = [];

        public Action? AfterFirstChunk { get; set; }
        public Action? BeforeLookup { get; set; }
        public bool Fail { get; set; }

        public void Match(int row, DomainCustomer existing, string matchedBy) =>
            _matches.Add(new DuplicateMatch(row, existing.Id, existing.DisplayName, matchedBy));

        public Task<IReadOnlyList<DuplicateMatch>> FindDuplicatesAsync(
            Guid tenantId,
            IReadOnlyList<ImportCustomerRow> chunk,
            CancellationToken ct
        )
        {
            BeforeLookup?.Invoke();
            ct.ThrowIfCancellationRequested();
            if (Fail)
                throw new InvalidOperationException("detector down");
            steps.Add("detect");
            AfterFirstChunk?.Invoke();
            AfterFirstChunk = null;
            return Task.FromResult<IReadOnlyList<DuplicateMatch>>(_matches);
        }

        public Task<DuplicateMatch?> FindDuplicateAsync(
            Guid tenantId,
            CustomerDuplicateCandidate candidate,
            Guid? excludeCustomerId,
            CancellationToken ct
        ) => throw new NotSupportedException();
    }

    private sealed class TakenIdentifiers : ICustomerImportIdentifierRegistry
    {
        public Dictionary<string, Guid> TaxIdOwners { get; } = [];
        public Dictionary<string, Guid> SpouseTaxIdRelations { get; } = [];
        public Dictionary<string, Guid> EmailOwners { get; } = [];

        public Task<ImportTakenIdentifiers> FindTakenAsync(
            Guid tenantId,
            IReadOnlyList<ImportCustomerRow> chunk,
            CancellationToken ct
        ) => Task.FromResult(new ImportTakenIdentifiers(TaxIdOwners, SpouseTaxIdRelations, EmailOwners));
    }

    private sealed class ScriptedCatalogs : ICatalogResolver
    {
        public bool Unknown { get; set; }

        public Task<Guid?> ResolveOccupationIdAsync(string occupationName, CancellationToken ct) =>
            Task.FromResult(Unknown ? null : (Guid?)Guid.NewGuid());

        public Task<Guid?> ResolvePrincipalBusinessActivityIdAsync(string naicsCode, CancellationToken ct) =>
            Task.FromResult(Unknown ? null : (Guid?)Guid.NewGuid());
    }

    private sealed class StepLock(List<string> steps) : ICustomerCreationLock
    {
        public Task AcquireAsync(Guid tenantId, string normalizedEmail, CancellationToken ct)
        {
            steps.Add($"lock:{normalizedEmail}");
            return Task.CompletedTask;
        }
    }

    private sealed class ScriptedFiles : ICustomerImportCloudStorageClient
    {
        public bool FailDownload { get; set; }
        public bool FailDelete { get; set; }
        public int Downloads { get; private set; }
        public int Deletes { get; private set; }

        public Task<Result> UploadAsync(
            Guid tenantId,
            Guid fileId,
            byte[] content,
            string fileName,
            string contentType,
            Guid actorId,
            Wolverine.IMessageBus bus,
            CancellationToken ct = default
        ) => Task.FromResult(Result.Success());

        public Task<Result<byte[]>> DownloadAsync(Guid tenantId, Guid fileId, CancellationToken ct = default)
        {
            Downloads++;
            return Task.FromResult(
                FailDownload ? Result.Failure<byte[]>(new Error("Storage.Down", "boom")) : Result.Success<byte[]>([])
            );
        }

        public Task<Result> DeleteAsync(Guid tenantId, Guid fileId, CancellationToken ct = default)
        {
            Deletes++;
            return Task.FromResult(FailDelete ? Result.Failure(new Error("Storage.Down", "boom")) : Result.Success());
        }
    }

    private sealed class FixedRows(List<ImportCustomerRow> rows) : ICustomerImportReaderFactory, ICustomerImportReader
    {
        public ImportSourceKind SourceKind => ImportSourceKind.Csv;

        public ICustomerImportReader Resolve(ImportSourceKind sourceKind) => this;

        public async IAsyncEnumerable<ImportCustomerRow> ReadAsync(
            Stream stream,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct
        )
        {
            foreach (var row in rows)
                yield return row;
            await Task.CompletedTask;
        }
    }

    private sealed class TestProtector : ISensitiveDataProtector
    {
        public short CurrentKeyVersion => 1;

        public byte[] Protect(string plainText) => [1, 2, 3];

        public string Unprotect(byte[] cipher) => string.Empty;

        public string ComputeBlindIndex(string plainText, Guid tenantId) => $"blind-{plainText}";

        public IReadOnlyCollection<string> ComputeBlindIndexCandidates(string plainText, Guid tenantId) => [];
    }

    private sealed class NoUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
    }

    private sealed class TestCorrelation : ICorrelationContext
    {
        public string CorrelationId { get; private set; } = "corr-import";

        public void Set(string correlationId) => CorrelationId = correlationId;

        public IDisposable Push(string correlationId)
        {
            CorrelationId = correlationId;
            return new NoScope();
        }

        private sealed class NoScope : IDisposable
        {
            public void Dispose() { }
        }
    }

    private sealed class UtcOfficeZone : IOfficeTimeZone
    {
        public Task<BuildingBlocks.TimeZones.ResolvedTimeZone> ResolveAsync(Guid tenantId, CancellationToken ct) =>
            Task.FromResult(BuildingBlocks.TimeZones.ResolvedTimeZone.Resolve(null));
    }
}
