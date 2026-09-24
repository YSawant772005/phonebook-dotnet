using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Phonebook.Api.Data;

namespace Phonebook.Api.Tests;

public sealed class PhonebookApiFactory : WebApplicationFactory<Program>
{
    private readonly InMemoryContactRepository _repository = new();

    public InMemoryContactRepository Repository => _repository;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<PhonebookDbContext>();
            services.RemoveAll<DbContextOptions<PhonebookDbContext>>();
            services.RemoveAll<IContactRepository>();
            services.RemoveAll<ContactRepository>();

            services.AddSingleton<IContactRepository>(_repository);
        });
    }
}