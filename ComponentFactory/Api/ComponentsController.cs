using ComponentFactory.Application.Abstractions;
using ComponentFactory.Application.Models;
using ComponentFactory.Domain;
using Microsoft.AspNetCore.Mvc;

namespace ComponentFactory.Api;

[ApiController]
[Route("api/components")]
[ServiceFilter(typeof(ApiKeyAuthorizationFilter))]
public sealed class ComponentsController(IComponentGenerator generator) : ControllerBase
{
    private readonly IComponentGenerator _generator = generator;

    [HttpPost]
    public async Task<ActionResult<GeneratedComponent>> CreateAsync(
        CreateComponentRequest request,
        CancellationToken cancellationToken)
    {
        var name = ComponentName.Create(request.ComponentName);
        var result = await _generator.GenerateAsync(name, cancellationToken);

        return Created(result.WebUrl, result);
    }
}
