using DuneFlame.Application.DTOs.Admin;
using DuneFlame.Application.Interfaces;
using DuneFlame.Domain.Enums;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DuneFlame.API.Controllers;

[Route("api/v1/admin/orders")]
[ApiController]
[Authorize(Roles = "Admin")]
public class AdminOrderController(
    IAdminOrderService adminOrderService,
    IValidator<UpdateOrderStatusRequest> updateStatusValidator) : ControllerBase
{
    private readonly IAdminOrderService _adminOrderService = adminOrderService;
    private readonly IValidator<UpdateOrderStatusRequest> _updateStatusValidator = updateStatusValidator;

    [HttpGet]
    public async Task<IActionResult> GetAllOrders()
    {
        try
        {
            var orders = await _adminOrderService.GetAllOrdersAsync();
            return Ok(orders);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id:guid}/status")]
    public async Task<IActionResult> UpdateOrderStatus(Guid id, [FromBody] UpdateOrderStatusRequest request)
    {
        var validationResult = await _updateStatusValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(validationResult.Errors);
        }

        try
        {
            // Convert int to OrderStatus enum
            if (!Enum.IsDefined(typeof(OrderStatus), request.Status))
            {
                return BadRequest(new { message = "Invalid order status value" });
            }

            var status = (OrderStatus)request.Status;
            await _adminOrderService.UpdateOrderStatusAsync(id, status);
            return Ok(new { message = "Order status updated successfully", status = status.ToString() });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> CancelOrder(Guid id)
    {
        try
        {
            await _adminOrderService.CancelOrderAsync(id);
            return Ok(new { message = "Order cancelled successfully with refund processed" });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
