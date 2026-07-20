using DuneFlame.Application.Orders.Commands.CancelOrder;
using DuneFlame.Application.Orders.Commands.CreateOrder;
using DuneFlame.Application.Orders.Queries.GetMyOrders;
using DuneFlame.Application.Orders.Queries.GetOrderById;
using DuneFlame.Domain.Exceptions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DuneFlame.API.Controllers;

[Route("api/v1/orders")]
[ApiController]
[Authorize]
public class OrderController(IMediator mediator) : ControllerBase
{
    private Guid? GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(claim, out var id) ? id : null;
    }

    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderCommand command)
    {
        try
        {
            var cmd = command with { UserId = GetUserId() };
            var order = await mediator.Send(cmd);
            return CreatedAtAction(nameof(GetOrderById), new { id = order.Id }, order);
        }
        catch (ConflictException ex) { return Conflict(new { message = ex.Message }); }
        catch (NotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (BadRequestException ex) { return BadRequest(new { message = ex.Message }); }
        catch (Exception ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpGet]
    public async Task<IActionResult> GetMyOrders()
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized(new { message = "User must be logged in to view orders" });
        var orders = await mediator.Send(new GetMyOrdersQuery(userId.Value));
        return Ok(orders);
    }

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetOrderById(Guid id)
    {
        try
        {
            var order = await mediator.Send(new GetOrderByIdQuery(id));
            var currentUserId = GetUserId();
            if (order.UserId.HasValue && (currentUserId == null || currentUserId != order.UserId))
                return Forbid();
            return Ok(order);
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (Exception ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("{id:guid}/cancel")]
    [AllowAnonymous]
    public async Task<IActionResult> CancelOrder(Guid id)
    {
        try
        {
            await mediator.Send(new CancelOrderCommand(id));
            return Ok(new { message = "Order is cancelled" });
        }
        catch (Exception ex) { return BadRequest(new { message = ex.Message }); }
    }
}
