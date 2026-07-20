using DuneFlame.Application.DTOs.Admin;
using DuneFlame.Application.DTOs.Common;
using DuneFlame.Application.DTOs.Order;
using DuneFlame.Application.Interfaces;
using DuneFlame.Domain.Enums;
using DuneFlame.Domain.Exceptions;
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
     public async Task<ActionResult<PagedResult<AdminOrderListDto>>> GetAllOrders(
         [FromQuery] int pageNumber = 1,
         [FromQuery] int pageSize = 10,
         [FromQuery] OrderStatus? status = null,
         [FromQuery] string? search = null)
     {
         try
         {
             var result = await _adminOrderService.GetAllOrdersAsync(
                 pageNumber: pageNumber,
                 pageSize: pageSize,
                 status: status,
                 searchTerm: search);
             return Ok(result);
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
        catch (NotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (BadRequestException ex)
        {
            return BadRequest(new { message = ex.Message });
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
        catch (NotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (BadRequestException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Updates the delivery status of an order independently from the fulfillment status.
    /// Pre-existing: Pending=0, InTransit=1, Delivered=2, Returned=3.
    /// Quiqup states: ReadyForCollection=4, PickedUp=5, Cancelled=6, Failed=7.
    /// </summary>
    [HttpPut("{id:guid}/delivery-status")]
    public async Task<IActionResult> UpdateDeliveryStatus(Guid id, [FromBody] UpdateDeliveryStatusRequest request)
    {
        if (!Enum.IsDefined(typeof(DeliveryStatus), request.DeliveryStatus))
            return BadRequest(new { message = "Invalid delivery status value." });

        try
        {
            var deliveryStatus = (DeliveryStatus)request.DeliveryStatus;
            await _adminOrderService.UpdateDeliveryStatusAsync(id, deliveryStatus);
            return Ok(new { message = "Delivery status updated successfully.", deliveryStatus = deliveryStatus.ToString() });
        }
        catch (NotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}

/// <summary>Request body for the delivery-status update endpoint.</summary>
public record UpdateDeliveryStatusRequest(
    /// <summary>
    /// Integer value of the DeliveryStatus enum.
    /// Pre-existing: 0=Pending, 1=InTransit, 2=Delivered, 3=Returned.
    /// Quiqup: 4=ReadyForCollection, 5=PickedUp, 6=Cancelled, 7=Failed.
    /// </summary>
    int DeliveryStatus
);
