using Microsoft.AspNetCore.Mvc;
using Phonebook.Api.Dtos;
using Phonebook.Api.Services;

namespace Phonebook.Api.Controllers;

[ApiController]
[Route("contacts")]
public sealed class ContactsController : ControllerBase
{
    private readonly ContactService _service;

    public ContactsController(ContactService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<ContactPageResponse>> GetContacts(
        [FromQuery] int page = 0,
        [FromQuery] int size = 20,
        [FromQuery] string? search = null,
        [FromQuery] string? sort = null,
        CancellationToken cancellationToken = default)
    {
        ContactPageResponse result =
            await _service.GetContactsAsync(page, size, search, sort, cancellationToken);
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<ContactResponse>> CreateContact(
        [FromBody] ContactCreateRequest request,
        CancellationToken cancellationToken)
    {
        ContactResponse created =
            await _service.CreateContactAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetContact), new { id = created.Id }, created);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ContactResponse>> GetContact(
        int id,
        CancellationToken cancellationToken)
    {
        ContactResponse result = await _service.GetContactAsync(id, cancellationToken);
        return Ok(result);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ContactResponse>> UpdateContact(
        int id,
        [FromBody] ContactCreateRequest request,
        CancellationToken cancellationToken)
    {
        ContactResponse result =
            await _service.UpdateContactAsync(id, request, cancellationToken);
        return Ok(result);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteContact(
        int id,
        CancellationToken cancellationToken)
    {
        await _service.DeleteContactAsync(id, cancellationToken);
        return Ok(new { message = "Contact deleted successfully." });
    }
}