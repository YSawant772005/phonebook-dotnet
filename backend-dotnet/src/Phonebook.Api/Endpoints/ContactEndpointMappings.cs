using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Phonebook.Api.Dtos;
using Phonebook.Api.Services;

namespace Phonebook.Api.Endpoints;

public static class ContactEndpointMappings
{
    private const string InvalidJsonDetail = "Request body must contain valid JSON.";

    public static IEndpointRouteBuilder MapContactEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/contacts", GetContactsAsync);
        endpoints.MapPost("/contacts", CreateContactAsync);
        endpoints.MapGet("/contacts/{id}", GetContactAsync);
        endpoints.MapPut("/contacts/{id}", UpdateContactAsync);
        endpoints.MapDelete("/contacts/{id}", DeleteContactAsync);
        return endpoints;
    }

    private static async Task<IResult> GetContactsAsync(
        ContactService service,
        int page = 0,
        int size = 20,
        string? search = null,
        string? sort = null,
        CancellationToken cancellationToken = default)
    {
        ContactPageResponse result = await service.GetContactsAsync(page, size, search, sort, cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> CreateContactAsync(
        HttpRequest request,
        ContactService service,
        CancellationToken cancellationToken)
    {
        ContactCreateRequest? contactRequest = await ReadRequestAsync(request, cancellationToken);
        if (contactRequest is null)
        {
            return InvalidJson();
        }

        IResult? validationResult = ValidateRequest(contactRequest);
        if (validationResult is not null)
        {
            return validationResult;
        }

        ContactResponse result = await service.CreateContactAsync(contactRequest, cancellationToken);
        return Results.Created($"/contacts/{result.Id}", result);
    }

    private static async Task<IResult> GetContactAsync(
        int id,
        ContactService service,
        CancellationToken cancellationToken)
    {
        ContactResponse result = await service.GetContactAsync(id, cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> UpdateContactAsync(
        int id,
        HttpRequest request,
        ContactService service,
        CancellationToken cancellationToken)
    {
        ContactCreateRequest? contactRequest = await ReadRequestAsync(request, cancellationToken);
        if (contactRequest is null)
        {
            return InvalidJson();
        }

        IResult? validationResult = ValidateRequest(contactRequest);
        if (validationResult is not null)
        {
            return validationResult;
        }

        ContactResponse result = await service.UpdateContactAsync(id, contactRequest, cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> DeleteContactAsync(
        int id,
        ContactService service,
        CancellationToken cancellationToken)
    {
        await service.DeleteContactAsync(id, cancellationToken);
        return Results.Ok(new { message = "Contact deleted successfully." });
    }

    private static async Task<ContactCreateRequest?> ReadRequestAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await request.ReadFromJsonAsync<ContactCreateRequest>(cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IResult? ValidateRequest(ContactCreateRequest request)
    {
        var validationResults = new List<ValidationResult>();
        if (Validator.TryValidateObject(request, new ValidationContext(request), validationResults, true))
        {
            return null;
        }

        string detail = validationResults
            .Select(result => result.ErrorMessage)
            .FirstOrDefault(message => !string.IsNullOrWhiteSpace(message))
            ?? "Invalid request.";

        return Results.BadRequest(new { detail });
    }

    private static IResult InvalidJson()
    {
        return Results.BadRequest(new { detail = InvalidJsonDetail });
    }
}
