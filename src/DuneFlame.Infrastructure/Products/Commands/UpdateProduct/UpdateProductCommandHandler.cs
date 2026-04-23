using DuneFlame.Application.Products.Commands.UpdateProduct;
using DuneFlame.Domain.Entities;
using DuneFlame.Domain.Enums;
using DuneFlame.Infrastructure.Persistence;
using DuneFlame.Infrastructure.Products.Commands.UpdateProduct.Strategies;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DuneFlame.Infrastructure.Products.Commands.UpdateProduct;

public class UpdateProductCommandHandler : IRequestHandler<UpdateProductCommand, bool>
{
    private readonly AppDbContext _context;
    private readonly IEnumerable<IProductUpdateStrategy> _strategies;
    private readonly HybridCache _cache;
    private readonly DuneFlame.Application.Interfaces.IFileService _fileService;
    private readonly ILogger<UpdateProductCommandHandler> _logger;

    public UpdateProductCommandHandler(
        AppDbContext context,
        IEnumerable<IProductUpdateStrategy> strategies,
        HybridCache cache,
        DuneFlame.Application.Interfaces.IFileService fileService,
        ILogger<UpdateProductCommandHandler> logger)
    {
        _context = context;
        _strategies = strategies;
        _cache = cache;
        _fileService = fileService;
        _logger = logger;
    }

    public async Task<bool> Handle(UpdateProductCommand request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Starting Handle process for UpdateProductCommand. Product ID: {Id}", request.Id);
            _logger.LogInformation("Incoming Translation Langs: {Ids}", string.Join(",", request.Translations?.Select(t => t.LanguageCode) ?? Array.Empty<string>()));
            _logger.LogInformation("Incoming Variant IDs: {Ids}", string.Join(",", request.Variants?.Select(v => v.Id?.ToString() ?? "null") ?? Array.Empty<string>()));

            // Robustly handle SpecificationsJson (deserialize if present)
            if (!string.IsNullOrWhiteSpace(request.SpecificationsJson))
            {
                try
                {
                    request.Specifications = JsonSerializer.Deserialize<Dictionary<string, string>>(request.SpecificationsJson);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to deserialize SpecificationsJson: {Json}", request.SpecificationsJson);
                    throw new ArgumentException("Invalid Specifications JSON format.");
                }
            }

            var product = await _context.Products
            .Include(p => p.Category)
            .Include(p => p.Translations)
            .Include(p => p.Images)
            .Include(p => p.Variants)
                .ThenInclude(v => v.Prices)
            .Include(p => p.Variants)
                .ThenInclude(v => v.Options)
            .Include(p => p.CoffeeProfile)
                .ThenInclude(cp => cp.RoastLevels)
            .Include(p => p.CoffeeProfile)
                .ThenInclude(cp => cp.GrindTypes)
            .Include(p => p.CoffeeProfile)
                .ThenInclude(cp => cp.FlavourNotes)
                    .ThenInclude(fn => fn.Translations)
            .Include(p => p.EquipmentProfile)
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);

            if (product == null)
                throw new KeyNotFoundException($"Product with ID {request.Id} not found.");

            if (product.CategoryId != request.CategoryId) product.CategoryId = request.CategoryId;
            if (product.BrandId != request.BrandId) product.BrandId = request.BrandId;

            // SAFEGUARD: Only update IsActive if the Frontend explicitly sends it as true or false.
            // Usually, in a FormData request, if a boolean is omitted, the framework defaults it.
            // Assuming IsActive is explicitly sent from frontend, otherwise do not blindly set it to false.
            product.IsActive = request.IsActive;

            if (request.Translations != null)
            {
                var existingTranslations = product.Translations.ToList();
                var orphanedTranslations = existingTranslations
                    .Where(e => !request.Translations.Any(r => r.LanguageCode == e.LanguageCode))
                    .ToList();

                if (orphanedTranslations.Any())
                {
                    _context.ProductTranslations.RemoveRange(orphanedTranslations);
                }

                foreach (var tDto in request.Translations)
                {
                    var existingTrans = product.Translations.FirstOrDefault(e => e.LanguageCode == tDto.LanguageCode);
                    if (existingTrans != null)
                    {
                        if (existingTrans.Name != tDto.Name) existingTrans.Name = tDto.Name;
                        if (existingTrans.Description != tDto.Description) existingTrans.Description = tDto.Description;
                    }
                    else
                    {
                        product.Translations.Add(new ProductTranslation
                        {
                            ProductId = product.Id,
                            LanguageCode = tDto.LanguageCode,
                            Name = tDto.Name,
                            Description = tDto.Description
                        });
                    }
                }
            }
            else
            {
                var existingEn = product.Translations.FirstOrDefault(e => e.LanguageCode == "en");
                if (existingEn != null)
                {
                    if (existingEn.Name != request.Name) existingEn.Name = request.Name;
                    if (existingEn.Description != request.Description) existingEn.Description = request.Description;
                }
                else
                {
                    product.Translations.Add(new ProductTranslation
                    {
                        LanguageCode = "en",
                        Name = request.Name,
                        Description = request.Description
                    });
                }
            }

            var existingVariants = product.Variants.ToList();

            // FIND ORPHANS (Variants to Delete)
            // If a variant exists in the DB but is NOT in the incoming array (either missing the object entirely or missing its ID)
            var orphanedVariants = existingVariants.Where(e => !request.Variants.Any(r => r.Id == e.Id)).ToList();

            if (orphanedVariants.Any())
            {
                _context.RemoveRange(orphanedVariants.SelectMany(v => v.Prices));
                _context.RemoveRange(orphanedVariants.SelectMany(v => v.Options));
                _context.RemoveRange(orphanedVariants);
            }

            foreach (var vDto in request.Variants)
            {
                if (vDto.Id.HasValue && vDto.Id.Value != Guid.Empty)
                {
                    var existingVar = existingVariants.FirstOrDefault(e => e.Id == vDto.Id);
                    if (existingVar != null)
                    {
                        if (existingVar.Sku != vDto.Sku) existingVar.Sku = vDto.Sku;
                        if (existingVar.StockQuantity != vDto.StockQuantity) existingVar.StockQuantity = vDto.StockQuantity;
                        
                        var existingOptions = existingVar.Options.ToList();
                        var orphanedOptions = existingOptions.Where(e => !vDto.Options.Any(o => o.ProductAttributeValueId == e.ProductAttributeValueId)).ToList();
                        if (orphanedOptions.Any()) _context.RemoveRange(orphanedOptions);
                        
                        foreach (var opt in vDto.Options)
                        {
                            if (!existingOptions.Any(e => e.ProductAttributeValueId == opt.ProductAttributeValueId))
                            {
                                var newOpt = new ProductVariantOption
                                {
                                    ProductVariantId = existingVar.Id,
                                    ProductAttributeValueId = opt.ProductAttributeValueId
                                };
                                _context.Entry(newOpt).State = EntityState.Added; // SAFEGUARD For ID Generation Strategy!
                                existingVar.Options.Add(newOpt);
                            }
                        }

                        if (vDto.Prices != null)
                        {
                            var existingPrices = existingVar.Prices.ToList();
                            var orphanedPrices = existingPrices.Where(e => !vDto.Prices.Any(p => Enum.TryParse<Currency>(p.CurrencyCode, true, out var c) && c == e.Currency)).ToList();
                            if (orphanedPrices.Any()) _context.RemoveRange(orphanedPrices);
                            
                            foreach (var priceDto in vDto.Prices)
                            {
                                if (Enum.TryParse<Currency>(priceDto.CurrencyCode, true, out var currencyCode))
                                {
                                    var existingPrice = existingPrices.FirstOrDefault(e => e.Currency == currencyCode);
                                    if (existingPrice != null)
                                    {
                                        if (existingPrice.Price != priceDto.Price) existingPrice.Price = priceDto.Price;
                                    }
                                    else
                                    {
                                        var newPrice = new ProductVariantPrice
                                        {
                                            ProductVariantId = existingVar.Id,
                                            Currency = currencyCode,
                                            Price = priceDto.Price
                                        };
                                        _context.Entry(newPrice).State = EntityState.Added; // SAFEGUARD For BaseEntity Id Auto generation
                                        existingVar.Prices.Add(newPrice);
                                    }
                                }
                            }
                        }
                    }
                }
                else
                {
                    var newVar = new ProductVariant
                    {
                        Sku = vDto.Sku,
                        StockQuantity = vDto.StockQuantity,
                        Options = vDto.Options?.Select(o => new ProductVariantOption { ProductAttributeValueId = o.ProductAttributeValueId }).ToList() ?? new(),
                        Prices = vDto.Prices?.Select(p => new ProductVariantPrice 
                        { 
                            Currency = Enum.Parse<Currency>(p.CurrencyCode, true), 
                            Price = p.Price 
                        }).ToList() ?? new()
                    };

                    // CRITICAL: Force EF Core to treat these entities as Added, preventing "Modified" state 0-row exceptions 
                    _context.Entry(newVar).State = EntityState.Added;
                    foreach(var opt in newVar.Options) _context.Entry(opt).State = EntityState.Added;
                    foreach(var price in newVar.Prices) _context.Entry(price).State = EntityState.Added;

                    product.Variants.Add(newVar);
                }
            }

            // Image Handling
            if (request.DeletedImageIds != null && request.DeletedImageIds.Any())
            {
                var imagesToDelete = product.Images.Where(i => request.DeletedImageIds.Contains(i.Id)).ToList();
                foreach (var img in imagesToDelete)
                {
                    _fileService.DeleteFile(img.ImageUrl);
                    product.Images.Remove(img);
                }
            }

            if (request.SetMainImageId.HasValue)
            {
                // Only update IsMain flag for existing images, don't accidentally touch orphaned/deleted references
                var existingImagesToUpdate = product.Images.Where(i => i.Id != Guid.Empty).ToList();
                foreach (var img in existingImagesToUpdate)
                {
                    var shouldBeMain = (img.Id == request.SetMainImageId.Value);
                    if (img.IsMain != shouldBeMain)
                    {
                        img.IsMain = shouldBeMain;
                    }
                }
            }

            if (request.Images != null && request.Images.Any())
            {
                var hasMainImage = product.Images.Any(i => i.IsMain);
                for (int i = 0; i < request.Images.Count; i++)
                {
                    var fileUrl = await _fileService.UploadImageAsync(request.Images[i], "products");
                    var newImage = new ProductImage
                    {
                        ProductId = product.Id,
                        ImageUrl = fileUrl,
                        IsMain = (!hasMainImage && i == 0) // First one is main if there's no main image yet
                    };
                    _context.Entry(newImage).State = EntityState.Added; // SAFEGUARD For ID Generation Strategy!
                    product.Images.Add(newImage);
                }
            }

            // Find and apply the correct update strategy
            var strategy = _strategies.FirstOrDefault(s => s.CanHandle(product.Category));
            if (strategy != null)
            {
                await strategy.ApplyUpdateAsync(product, request, _context);
            }

            foreach (var entry in _context.ChangeTracker.Entries()) 
            {
                var isDictionary = entry.Metadata.ClrType == typeof(Dictionary<string, object>);
                var id = isDictionary ? "M2M_CompositeKey" : entry.Property("Id")?.CurrentValue?.ToString();

                _logger.LogInformation("Entity: {Entity}, State: {State}, ID: {Id}", 
                    entry.Metadata.Name, entry.State, id);
            }

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Product Update SaveChangesAsync completed successfully.");
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _logger.LogError(ex, "Concurrency exception during save! Details:");
                foreach (var entry in ex.Entries)
                {
                    var id = entry.Property("Id")?.CurrentValue;
                    _logger.LogError("Failed Entity: {EntityName}, ID: {Id}, State: {State}",
                        entry.Metadata.Name, id, entry.State);
                }
                throw;
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "DbUpdateException occurred during SaveChangesAsync!");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unexpected error occurred during SaveChangesAsync!");
                throw;
            }

            await _cache.RemoveByTagAsync($"product-tag:{product.Id}", cancellationToken);
            await _cache.RemoveByTagAsync($"product-tag:slug:{product.Slug}", cancellationToken);
            await _cache.RemoveByTagAsync("product-tag:list", cancellationToken);
            await _cache.RemoveByTagAsync("product-tag:admin-list", cancellationToken);

            _logger.LogInformation("Update product handler completely finished. Validation, DB commit, and Cache wipe complete.");
            return true;
        }
        catch (Exception outerEx)
        {
            _logger.LogError(outerEx, "UpdateProductCommandHandler FATAL EXCEPTION: {Message}", outerEx.Message);
            throw;
        }
    }
}
