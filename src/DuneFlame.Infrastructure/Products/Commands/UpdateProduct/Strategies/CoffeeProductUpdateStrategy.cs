using DuneFlame.Application.Products.Commands.UpdateProduct;
using DuneFlame.Domain.Entities;
using DuneFlame.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DuneFlame.Infrastructure.Products.Commands.UpdateProduct.Strategies;

public class CoffeeProductUpdateStrategy : IProductUpdateStrategy
{
    private readonly ILogger<CoffeeProductUpdateStrategy> _logger;

    public CoffeeProductUpdateStrategy(ILogger<CoffeeProductUpdateStrategy> logger)
    {
        _logger = logger;
    }

    public bool CanHandle(Category category) => category.IsCoffeeCategory;

    public async Task ApplyUpdateAsync(Product product, UpdateProductCommand command, AppDbContext context)
    {
        try
        {
            _logger.LogInformation("Starting CoffeeProductUpdateStrategy...");

            // Ensure CoffeeProfile exists
            if (product.CoffeeProfile == null)
            {
                _logger.LogInformation("Creating new CoffeeProfile for Product ID: {Id}", product.Id);
                product.CoffeeProfile = new ProductCoffeeProfile { ProductId = product.Id };
                context.Add(product.CoffeeProfile);
            }

        var profile = product.CoffeeProfile;

        // Origin Update
        if (profile.OriginId != command.OriginId)
        {
            profile.OriginId = command.OriginId;
        }

        // Delta Merge: RoastLevels
        if (command.RoastLevelIds != null)
        {
            _logger.LogInformation("Processing RoastLevels. Incoming count: {Count}", command.RoastLevelIds.Count);
            var existingRoastLevels = profile.RoastLevels.ToList();
            foreach (var existing in existingRoastLevels)
            {
                if (!command.RoastLevelIds.Contains(existing.Id))
                {
                    _logger.LogInformation("Removing RoastLevel: {Id}", existing.Id);
                    profile.RoastLevels.Remove(existing);
                }
            }
            foreach (var id in command.RoastLevelIds)
            {
                if (!existingRoastLevels.Any(r => r.Id == id))
                {
                    var roastEntity = await context.Set<RoastLevelEntity>().FindAsync(id);
                    if (roastEntity != null)
                    {
                        _logger.LogInformation("Adding RoastLevel: {Id}", id);
                        profile.RoastLevels.Add(roastEntity);

                        // Force Added state on the junction table if necessary.
                    }
                }
            }
        }

        // Delta Merge: GrindTypes
        if (command.GrindTypeIds != null)
        {
            _logger.LogInformation("Processing GrindTypes. Incoming count: {Count}", command.GrindTypeIds.Count);
            var existingGrindTypes = profile.GrindTypes.ToList();
            foreach (var existing in existingGrindTypes)
            {
                if (!command.GrindTypeIds.Contains(existing.Id))
                {
                    _logger.LogInformation("Removing GrindType: {Id}", existing.Id);
                    profile.GrindTypes.Remove(existing);
                }
            }
            foreach (var id in command.GrindTypeIds)
            {
                if (!existingGrindTypes.Any(g => g.Id == id))
                {
                    var grindType = await context.Set<GrindType>().FindAsync(id);
                    if (grindType != null)
                    {
                        _logger.LogInformation("Adding GrindType: {Id}", id);
                        profile.GrindTypes.Add(grindType);

                        // Force Added state on the junction table if necessary.
                    }
                }
            }
        }

        // Delta Merge: FlavourNotes and Translations
        if (command.FlavourNotes != null)
        {
            _logger.LogInformation("Processing FlavourNotes. Incoming count: {Count}", command.FlavourNotes.Count);
            var existingNotes = profile.FlavourNotes.ToList();

            // Remove orphans
            var orphanedNotes = existingNotes.Where(e => !command.FlavourNotes.Any(c => c.Id == e.Id)).ToList();
            if (orphanedNotes.Any())
            {
                _logger.LogInformation("Removing {Count} orphaned FlavourNotes.", orphanedNotes.Count);
                context.RemoveRange(orphanedNotes);
                foreach (var note in orphanedNotes)
                {
                    profile.FlavourNotes.Remove(note);
                }
            }

            // Update/Insert notes
            foreach (var dto in command.FlavourNotes)
            {
                if (dto.Id.HasValue && dto.Id.Value != Guid.Empty)
                {
                    var existingNote = existingNotes.FirstOrDefault(e => e.Id == dto.Id);
                    if (existingNote != null)
                    {
                        if (existingNote.Name != dto.Name) existingNote.Name = dto.Name;
                        if (existingNote.DisplayOrder != dto.DisplayOrder) existingNote.DisplayOrder = dto.DisplayOrder;

                        if (dto.Translations != null)
                        {
                            var existingTranslations = existingNote.Translations.ToList();
                            var orphanedTranslations = existingTranslations
                                .Where(et => !dto.Translations.Any(dt => dt.LanguageCode == et.LanguageCode))
                                .ToList();

                            if (orphanedTranslations.Any())
                            {
                                context.RemoveRange(orphanedTranslations);
                            }

                            foreach (var tDto in dto.Translations)
                            {
                                var existingTrans = existingNote.Translations.FirstOrDefault(t => t.LanguageCode == tDto.LanguageCode);
                                if (existingTrans != null)
                                {
                                    _logger.LogInformation("Updating FlavourNoteTranslation: NoteId={Id}, Lang={Lang}", existingNote.Id, tDto.LanguageCode);
                                    if (existingTrans.Name != tDto.Name) existingTrans.Name = tDto.Name;
                                }
                                else
                                {
                                    _logger.LogInformation("Adding FlavourNoteTranslation for NoteId={Id}, Lang={Lang}", existingNote.Id, tDto.LanguageCode);
                                    var newTrans = new FlavourNoteTranslation
                                    {
                                        FlavourNoteId = existingNote.Id,
                                        LanguageCode = tDto.LanguageCode,
                                        Name = tDto.Name
                                    };
                                    context.Entry(newTrans).State = EntityState.Added; // SAFEGUARD! 
                                    existingNote.Translations.Add(newTrans);
                                }
                            }
                        }
                    }
                }
                else
                {
                    var newNote = new FlavourNote
                    {
                        Name = dto.Name,
                        DisplayOrder = dto.DisplayOrder,
                        Translations = dto.Translations?.Select(t => new FlavourNoteTranslation
                        {
                            LanguageCode = t.LanguageCode,
                            Name = t.Name
                        }).ToList() ?? new List<FlavourNoteTranslation>()
                    };
                    context.Entry(newNote).State = EntityState.Added; // SAFEGUARD!
                    foreach(var nt in newNote.Translations) context.Entry(nt).State = EntityState.Added;

                    profile.FlavourNotes.Add(newNote);
                    _logger.LogInformation("Added new FlavourNote: {Name}", dto.Name);
                }
            }
        }

        _logger.LogInformation("Successfully completed CoffeeProductUpdateStrategy.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred during CoffeeProductUpdateStrategy execution.");
            throw;
        }
    }
}
