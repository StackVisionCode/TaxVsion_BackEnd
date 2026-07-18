using System.Runtime.CompilerServices;

// Fase 1 (hardening) — habilita testear directamente CorrespondenceCustomerClient (internal) contra
// un HttpMessageHandler fake que simula HttpRequestException/TaskCanceledException, mismo objetivo
// que CloudStorageOutboundAttachmentFetcherTests en Postmaster (esa clase es pública ahí; acá se
// preserva el internal del resto de los clientes concretos de este proyecto y en cambio se abre
// visibilidad solo al proyecto de tests).
[assembly: InternalsVisibleTo("TaxVision.Correspondence.Tests")]
