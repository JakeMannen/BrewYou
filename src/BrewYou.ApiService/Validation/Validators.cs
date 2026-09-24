using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Endpoints;
using BrewYou.ApiService.Services;
using FluentValidation;

namespace BrewYou.ApiService.Validation;

public class RecipeIngredientInputDtoValidator : AbstractValidator<RecipeIngredientInputDto>
{
    public RecipeIngredientInputDtoValidator()
    {
        RuleFor(x => x.IngredientId).NotEmpty().WithMessage("Ingredient ID is required.");
        RuleFor(x => x.Amount).GreaterThan(0).WithMessage("Ingredient amount must be greater than 0.");
        RuleFor(x => x.Unit).NotEmpty().MaximumLength(20).WithMessage("Unit is required and cannot exceed 20 characters.");
        RuleFor(x => x.DurationMinutes)
            .GreaterThanOrEqualTo(0)
            .When(x => x.DurationMinutes.HasValue)
            .WithMessage("Duration minutes cannot be negative.");
        RuleFor(x => x.Notes)
            .MaximumLength(1000).WithMessage("Notes cannot exceed 1000 characters.")
            .Must(notes => string.IsNullOrEmpty(notes) || (!notes.Contains('<') && !notes.Contains('>')))
            .WithMessage("Notes contain forbidden characters.");
        RuleFor(x => x.Form)
            .MaximumLength(50).WithMessage("Form cannot exceed 50 characters.")
            .Must(f => string.IsNullOrEmpty(f) || (!f.Contains('<') && !f.Contains('>')))
            .WithMessage("Form contains forbidden characters.");
    }
}

public class BatchIngredientInputDtoValidator : AbstractValidator<BatchIngredientInputDto>
{
    public BatchIngredientInputDtoValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100).WithMessage("Ingredient name is required.");
        RuleFor(x => x.Amount).GreaterThan(0).WithMessage("Ingredient amount must be greater than 0.");
        RuleFor(x => x.Unit).NotEmpty().MaximumLength(20).WithMessage("Unit is required and cannot exceed 20 characters.");
        RuleFor(x => x.AdditionTimeMinutes)
            .GreaterThanOrEqualTo(0)
            .When(x => x.AdditionTimeMinutes.HasValue)
            .WithMessage("Addition time cannot be negative.");
        RuleFor(x => x.Notes)
            .MaximumLength(1000).WithMessage("Notes cannot exceed 1000 characters.")
            .Must(notes => string.IsNullOrEmpty(notes) || (!notes.Contains('<') && !notes.Contains('>')))
            .WithMessage("Notes contain forbidden characters.");
        RuleFor(x => x.Form)
            .MaximumLength(50).WithMessage("Form cannot exceed 50 characters.")
            .Must(f => string.IsNullOrEmpty(f) || (!f.Contains('<') && !f.Contains('>')))
            .WithMessage("Form contains forbidden characters.");
    }
}

public class CalculateRecipeRequestValidator : AbstractValidator<CalculateRecipeRequest>
{
    public CalculateRecipeRequestValidator()
    {
        RuleFor(x => x.BatchSizeLiters).GreaterThan(0).WithMessage("Batch size must be greater than 0 liters.");
        RuleFor(x => x.EfficiencyPercent)
            .InclusiveBetween(1, 100)
            .WithMessage("Efficiency percent must be between 1 and 100.");
        RuleFor(x => x.BoilTimeMinutes)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Boil time minutes cannot be negative.");
        RuleForEach(x => x.Ingredients).SetValidator(new RecipeIngredientInputDtoValidator());
    }
}

public class RecipeMashStepInputDtoValidator : AbstractValidator<RecipeMashStepInputDto>
{
    public RecipeMashStepInputDtoValidator()
    {
        RuleFor(x => x.StepOrder)
            .InclusiveBetween(1, 15)
            .WithMessage("Mash step order must be between 1 and 15.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Mash step name is required.")
            .MaximumLength(100).WithMessage("Mash step name cannot exceed 100 characters.")
            .Must(name => !name.Contains('<') && !name.Contains('>'))
            .WithMessage("Mash step name contains invalid characters.");

        RuleFor(x => x.Type)
            .IsInEnum().WithMessage("Invalid mash step type.");

        RuleFor(x => x.TemperatureC)
            .InclusiveBetween(20.0m, 100.0m)
            .WithMessage("Mash step temperature must be between 20°C and 100°C.");

        RuleFor(x => x.DurationMinutes)
            .InclusiveBetween(0, 360)
            .WithMessage("Mash step duration must be between 0 and 360 minutes.");

        RuleFor(x => x.RampTimeMinutes)
            .InclusiveBetween(0, 180)
            .When(x => x.RampTimeMinutes.HasValue)
            .WithMessage("Ramp time must be between 0 and 180 minutes.");

        RuleFor(x => x.InfuseAmountLiters)
            .InclusiveBetween(0.0m, 500.0m)
            .When(x => x.InfuseAmountLiters.HasValue)
            .WithMessage("Infuse amount must be between 0 and 500 liters.");

        RuleFor(x => x.Notes)
            .MaximumLength(1000).WithMessage("Notes cannot exceed 1000 characters.")
            .Must(notes => string.IsNullOrEmpty(notes) || (!notes.Contains('<') && !notes.Contains('>')))
            .WithMessage("Notes contain forbidden characters.");
    }
}

public class RecipeFermentationStepInputDtoValidator : AbstractValidator<RecipeFermentationStepInputDto>
{
    public RecipeFermentationStepInputDtoValidator()
    {
        RuleFor(x => x.StepOrder)
            .InclusiveBetween(1, 20)
            .WithMessage("Fermentation step order must be between 1 and 20.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Fermentation step name is required.")
            .MaximumLength(100).WithMessage("Fermentation step name cannot exceed 100 characters.")
            .Must(name => !name.Contains('<') && !name.Contains('>'))
            .WithMessage("Fermentation step name contains invalid characters.");

        RuleFor(x => x.Type)
            .IsInEnum().WithMessage("Invalid fermentation step type.");

        RuleFor(x => x.TargetTemperatureC)
            .InclusiveBetween(-5.0m, 45.0m)
            .WithMessage("Target temperature must be between -5°C and 45°C.");

        RuleFor(x => x.DurationDays)
            .InclusiveBetween(1, 365)
            .WithMessage("Fermentation step duration must be between 1 and 365 days.");

        RuleFor(x => x.RampTimeHours)
            .InclusiveBetween(0, 168)
            .When(x => x.RampTimeHours.HasValue)
            .WithMessage("Ramp time must be between 0 and 168 hours.");

        RuleFor(x => x.TriggerGravity)
            .InclusiveBetween(0.980m, 1.200m)
            .When(x => x.TriggerGravity.HasValue)
            .WithMessage("Trigger gravity must be between 0.980 and 1.200.");

        RuleFor(x => x.Notes)
            .MaximumLength(1000).WithMessage("Notes cannot exceed 1000 characters.")
            .Must(notes => string.IsNullOrEmpty(notes) || (!notes.Contains('<') && !notes.Contains('>')))
            .WithMessage("Notes contain forbidden characters.");
    }
}

public class CreateRecipeRequestValidator : AbstractValidator<CreateRecipeRequest>
{
    public CreateRecipeRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Recipe name is required.")
            .MaximumLength(100).WithMessage("Recipe name cannot exceed 100 characters.")
            .Must(name => !name.Contains('<') && !name.Contains('>'))
            .WithMessage("Recipe name contains invalid characters.");
        RuleFor(x => x.BeerStyle)
            .MaximumLength(100).WithMessage("Beer style cannot exceed 100 characters.")
            .Must(style => string.IsNullOrEmpty(style) || (!style.Contains('<') && !style.Contains('>')))
            .WithMessage("Beer style contains invalid characters.");
        RuleFor(x => x.Description)
            .MaximumLength(2000).WithMessage("Description cannot exceed 2000 characters.")
            .Must(desc => string.IsNullOrEmpty(desc) || (!desc.Contains('<') && !desc.Contains('>')))
            .WithMessage("Description contains invalid characters.");
        RuleFor(x => x.BatchSizeLiters).GreaterThan(0).WithMessage("Batch size must be greater than 0 liters.");
        RuleFor(x => x.BoilTimeMinutes).GreaterThanOrEqualTo(0).WithMessage("Boil time cannot be negative.");
        RuleFor(x => x.EfficiencyPercent)
            .InclusiveBetween(1, 100)
            .WithMessage("Efficiency percent must be between 1 and 100.");
        RuleForEach(x => x.Ingredients).SetValidator(new RecipeIngredientInputDtoValidator());
        RuleForEach(x => x.MashSteps).SetValidator(new RecipeMashStepInputDtoValidator());
        RuleFor(x => x.MashSteps)
            .Must(steps => steps == null || !steps.Any() || steps.Select(s => s.StepOrder).Distinct().Count() == steps.Count)
            .WithMessage("Mash step orders must be unique.");
        RuleForEach(x => x.FermentationSteps).SetValidator(new RecipeFermentationStepInputDtoValidator());
        RuleFor(x => x.FermentationSteps)
            .Must(steps => steps == null || !steps.Any() || steps.Select(s => s.StepOrder).Distinct().Count() == steps.Count)
            .WithMessage("Fermentation step orders must be unique.");
    }
}

public class UpdateRecipeRequestValidator : AbstractValidator<UpdateRecipeRequest>
{
    public UpdateRecipeRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Recipe name is required.")
            .MaximumLength(100).WithMessage("Recipe name cannot exceed 100 characters.")
            .Must(name => !name.Contains('<') && !name.Contains('>'))
            .WithMessage("Recipe name contains invalid characters.");
        RuleFor(x => x.BeerStyle)
            .MaximumLength(100).WithMessage("Beer style cannot exceed 100 characters.")
            .Must(style => string.IsNullOrEmpty(style) || (!style.Contains('<') && !style.Contains('>')))
            .WithMessage("Beer style contains invalid characters.");
        RuleFor(x => x.Description)
            .MaximumLength(2000).WithMessage("Description cannot exceed 2000 characters.")
            .Must(desc => string.IsNullOrEmpty(desc) || (!desc.Contains('<') && !desc.Contains('>')))
            .WithMessage("Description contains invalid characters.");
        RuleFor(x => x.BatchSizeLiters).GreaterThan(0).WithMessage("Batch size must be greater than 0 liters.");
        RuleFor(x => x.BoilTimeMinutes).GreaterThanOrEqualTo(0).WithMessage("Boil time cannot be negative.");
        RuleFor(x => x.EfficiencyPercent)
            .InclusiveBetween(1, 100)
            .WithMessage("Efficiency percent must be between 1 and 100.");
        RuleForEach(x => x.Ingredients).SetValidator(new RecipeIngredientInputDtoValidator());
        RuleForEach(x => x.MashSteps).SetValidator(new RecipeMashStepInputDtoValidator());
        RuleFor(x => x.MashSteps)
            .Must(steps => steps == null || !steps.Any() || steps.Select(s => s.StepOrder).Distinct().Count() == steps.Count)
            .WithMessage("Mash step orders must be unique.");
        RuleForEach(x => x.FermentationSteps).SetValidator(new RecipeFermentationStepInputDtoValidator());
        RuleFor(x => x.FermentationSteps)
            .Must(steps => steps == null || !steps.Any() || steps.Select(s => s.StepOrder).Distinct().Count() == steps.Count)
            .WithMessage("Fermentation step orders must be unique.");
    }
}

public class CreateIngredientRequestValidator : AbstractValidator<CreateIngredientRequest>
{
    public CreateIngredientRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Ingredient name is required.")
            .MinimumLength(2).WithMessage("Ingredient name must be at least 2 characters long.")
            .MaximumLength(100).WithMessage("Ingredient name cannot exceed 100 characters.")
            .Must(name => !name.Any(char.IsControl))
            .WithMessage("Ingredient name cannot contain control characters.")
            .Must(name => !name.Contains('<') && !name.Contains('>'))
            .WithMessage("Ingredient name contains invalid characters.");
        RuleFor(x => x.Description)
            .MaximumLength(1000).WithMessage("Description cannot exceed 1000 characters.")
            .Must(desc => string.IsNullOrEmpty(desc) || !desc.Any(c => char.IsControl(c) && c != '\r' && c != '\n' && c != '\t'))
            .WithMessage("Description cannot contain control characters.")
            .Must(desc => string.IsNullOrEmpty(desc) || (!desc.Contains('<') && !desc.Contains('>')))
            .WithMessage("Description contains invalid characters.");
        RuleFor(x => x.Type)
            .IsInEnum().WithMessage("A valid ingredient type is required.");
        RuleFor(x => x.PotentialGravity)
            .InclusiveBetween(1.000m, 1.200m)
            .When(x => x.PotentialGravity.HasValue)
            .WithMessage("Potential gravity must be between 1.000 and 1.200.");
        RuleFor(x => x.ColorSrm)
            .InclusiveBetween(0m, 1000m)
            .When(x => x.ColorSrm.HasValue)
            .WithMessage("Color SRM must be between 0 and 1000.");
        RuleFor(x => x.AlphaAcidPercent)
            .InclusiveBetween(0, 100)
            .When(x => x.AlphaAcidPercent.HasValue)
            .WithMessage("Alpha acid percent must be between 0 and 100.");
        RuleFor(x => x.AttenuationPercent)
            .InclusiveBetween(0, 100)
            .When(x => x.AttenuationPercent.HasValue)
            .WithMessage("Attenuation percent must be between 0 and 100.");
        RuleFor(x => x.InitialStock)
            .GreaterThanOrEqualTo(0)
            .When(x => x.InitialStock.HasValue)
            .WithMessage("Initial stock cannot be negative.");
        RuleFor(x => x.StockUnit)
            .MaximumLength(20).When(x => !string.IsNullOrEmpty(x.StockUnit))
            .WithMessage("Stock unit cannot exceed 20 characters.")
            .Must(u => string.IsNullOrEmpty(u) || (!u.Contains('<') && !u.Contains('>')))
            .WithMessage("Stock unit contains invalid characters.");
        RuleFor(x => x.Form)
            .MaximumLength(50).WithMessage("Form cannot exceed 50 characters.")
            .Must(f => string.IsNullOrEmpty(f) || (!f.Contains('<') && !f.Contains('>')))
            .WithMessage("Form contains forbidden characters.");
    }
}

public class UpdateIngredientStockRequestValidator : AbstractValidator<UpdateIngredientStockRequest>
{
    public UpdateIngredientStockRequestValidator()
    {
        RuleFor(x => x.Amount)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Stock amount cannot be negative.");

        RuleFor(x => x.Unit)
            .MaximumLength(20).When(x => !string.IsNullOrEmpty(x.Unit))
            .WithMessage("Unit cannot exceed 20 characters.")
            .Must(u => string.IsNullOrEmpty(u) || (!u.Contains('<') && !u.Contains('>')))
            .WithMessage("Unit contains invalid characters.");
    }
}

public class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("A valid email is required.");
        RuleFor(x => x.Password).NotEmpty().WithMessage("Password is required.")
            .MinimumLength(6).WithMessage("Password must be at least 6 characters long.");
    }
}

public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("A valid email is required.");
        RuleFor(x => x.Password).NotEmpty().WithMessage("Password is required.");
    }
}

public class GoogleAuthRequestValidator : AbstractValidator<GoogleAuthRequest>
{
    public GoogleAuthRequestValidator()
    {
        RuleFor(x => x.IdToken)
            .NotEmpty().WithMessage("Google ID token is required.");

        RuleFor(x => x.PreferredLanguage)
            .MaximumLength(10).When(x => !string.IsNullOrEmpty(x.PreferredLanguage))
            .WithMessage("Preferred language code cannot exceed 10 characters.");
    }
}

public class CreateBrewerySetupRequestValidator : AbstractValidator<CreateBrewerySetupRequest>
{
    public CreateBrewerySetupRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Brewery setup name is required.")
            .MaximumLength(100).WithMessage("Brewery setup name cannot exceed 100 characters.")
            .Must(name => !name.Contains('<') && !name.Contains('>'))
            .WithMessage("Brewery setup name contains invalid characters.");

        RuleFor(x => x.Description)
            .MaximumLength(500).When(x => !string.IsNullOrEmpty(x.Description))
            .WithMessage("Description cannot exceed 500 characters.");
    }
}

public class UpdateBrewerySetupRequestValidator : AbstractValidator<UpdateBrewerySetupRequest>
{
    public UpdateBrewerySetupRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Brewery setup name is required.")
            .MaximumLength(100).WithMessage("Brewery setup name cannot exceed 100 characters.")
            .Must(name => !name.Contains('<') && !name.Contains('>'))
            .WithMessage("Brewery setup name contains invalid characters.");

        RuleFor(x => x.Description)
            .MaximumLength(500).When(x => !string.IsNullOrEmpty(x.Description))
            .WithMessage("Description cannot exceed 500 characters.");
    }
}

public class CreateEquipmentRequestValidator : AbstractValidator<CreateEquipmentRequest>
{
    public CreateEquipmentRequestValidator()
    {
        RuleFor(x => x.BrewerySetupId).NotEmpty().WithMessage("Brewery setup ID is required.");

        RuleFor(x => x.Name).NotEmpty().WithMessage("Equipment name is required.")
            .MaximumLength(100).WithMessage("Equipment name cannot exceed 100 characters.");

        RuleFor(x => x.Type).IsInEnum().WithMessage("A valid equipment type is required.");

        RuleFor(x => x.Subtype).IsInEnum().When(x => x.Subtype.HasValue)
            .WithMessage("Specified equipment subtype is invalid.");

        RuleFor(x => x)
            .Must(x => HaveValidSubtypeForType(x.Type, x.Subtype))
            .When(x => x.Subtype.HasValue)
            .WithMessage(x => $"EquipmentSubtype '{x.Subtype}' is not valid for EquipmentType '{x.Type}'.")
            .WithName("Subtype");

        RuleFor(x => x.Capacity)
            .GreaterThanOrEqualTo(0).WithMessage("Capacity cannot be negative.")
            .LessThanOrEqualTo(100_000).WithMessage("Capacity exceeds allowable limit.");

        RuleFor(x => x.Capacity)
            .GreaterThan(0).When(x => x.Type != EquipmentType.Other && x.Type != EquipmentType.Sensor)
            .WithMessage("Capacity must be greater than 0.");

        RuleFor(x => x.CurrentVolume)
            .GreaterThanOrEqualTo(0)
            .When(x => x.CurrentVolume.HasValue)
            .WithMessage("Current volume cannot be negative.");

        RuleFor(x => x)
            .Must(x => !x.CurrentVolume.HasValue || x.CurrentVolume.Value <= x.Capacity)
            .When(x => x.CurrentVolume.HasValue && x.Type != EquipmentType.Other && x.Type != EquipmentType.Sensor)
            .WithMessage(x => $"Current volume ({x.CurrentVolume}) cannot exceed equipment capacity ({x.Capacity}).")
            .WithName("CurrentVolume");

        RuleFor(x => x.Unit).IsInEnum().When(x => x.Unit.HasValue).WithMessage("Specified volume unit is invalid.");

        RuleFor(x => x.Description).MaximumLength(500).When(x => !string.IsNullOrEmpty(x.Description))
            .WithMessage("Description cannot exceed 500 characters.");

        RuleFor(x => x.Notes).MaximumLength(2000).When(x => !string.IsNullOrEmpty(x.Notes))
            .WithMessage("Notes cannot exceed 2,000 characters.");

        RuleFor(x => x.BoilOffRatePerHour)
            .InclusiveBetween(0m, 50m)
            .When(x => x.BoilOffRatePerHour.HasValue)
            .WithMessage("Boil-off rate must be between 0 and 50 L/hr.");

        RuleFor(x => x.BoilOffRatePerHour)
            .Must(val => !val.HasValue || val.Value == 0m)
            .When(x => x.Type == EquipmentType.Sensor)
            .WithMessage("Sensors cannot have a boil-off rate.");

        RuleFor(x => x.TrubLossLiters)
            .GreaterThanOrEqualTo(0m)
            .When(x => x.TrubLossLiters.HasValue)
            .WithMessage("Trub loss cannot be negative.");

        RuleFor(x => x.TrubLossLiters)
            .Must(val => !val.HasValue || val.Value == 0m)
            .When(x => x.Type == EquipmentType.Sensor)
            .WithMessage("Sensors cannot have trub loss.");

        RuleFor(x => x.MashTunDeadSpaceLiters)
            .GreaterThanOrEqualTo(0m)
            .When(x => x.MashTunDeadSpaceLiters.HasValue)
            .WithMessage("Mash tun dead space cannot be negative.");

        RuleFor(x => x.MashTunDeadSpaceLiters)
            .Must(val => !val.HasValue || val.Value == 0m)
            .When(x => x.Type == EquipmentType.Sensor)
            .WithMessage("Sensors cannot have mash tun dead space.");

        RuleFor(x => x.PackagingLossLiters)
            .GreaterThanOrEqualTo(0m)
            .When(x => x.PackagingLossLiters.HasValue)
            .WithMessage("Packaging loss cannot be negative.");

        RuleFor(x => x.PackagingLossLiters)
            .Must(val => !val.HasValue || val.Value == 0m)
            .When(x => x.Type == EquipmentType.Sensor)
            .WithMessage("Sensors cannot have packaging loss.");

        RuleFor(x => x.CurrentTemperatureC)
            .InclusiveBetween(-20m, 120m)
            .When(x => x.CurrentTemperatureC.HasValue)
            .WithMessage("Current temperature must be between -20 °C and 120 °C.");

        RuleFor(x => x.ConnectionType)
            .IsInEnum()
            .When(x => x.ConnectionType.HasValue)
            .WithMessage("Specified connection type is invalid.");

        RuleFor(x => x.ConnectionConfigJson)
            .MaximumLength(4000)
            .When(x => !string.IsNullOrEmpty(x.ConnectionConfigJson))
            .WithMessage("Connection configuration cannot exceed 4,000 characters.");
    }

    internal static bool HaveValidSubtypeForType(EquipmentType type, EquipmentSubtype? subtype)
    {
        if (!subtype.HasValue)
            return true;

        return type switch
        {
            EquipmentType.Boiler => subtype.Value is EquipmentSubtype.AllInOne or EquipmentSubtype.Pan or EquipmentSubtype.Hlt or EquipmentSubtype.HermsRims or EquipmentSubtype.Other,
            EquipmentType.Fermenter => subtype.Value is EquipmentSubtype.Bucket or EquipmentSubtype.ConicalFermenter or EquipmentSubtype.Carboy or EquipmentSubtype.PressureFermenter or EquipmentSubtype.StainlessBucket or EquipmentSubtype.Other,
            EquipmentType.Keg => subtype.Value is EquipmentSubtype.Cornelius or EquipmentSubtype.Minikeg or EquipmentSubtype.MiniBarrel or EquipmentSubtype.PetKeg or EquipmentSubtype.Other,
            EquipmentType.Sensor => subtype.Value is EquipmentSubtype.ISpindel or EquipmentSubtype.Tilt or EquipmentSubtype.GenericSensor,
            EquipmentType.Other => subtype.Value is EquipmentSubtype.Other,
            _ => true
        };
    }
}

public class UpdateEquipmentRequestValidator : AbstractValidator<UpdateEquipmentRequest>
{
    public UpdateEquipmentRequestValidator()
    {
        RuleFor(x => x.BrewerySetupId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("Brewery setup ID cannot be empty.");

        RuleFor(x => x.Name).NotEmpty().WithMessage("Equipment name is required.")
            .MaximumLength(100).WithMessage("Equipment name cannot exceed 100 characters.");

        RuleFor(x => x.Type).IsInEnum().WithMessage("A valid equipment type is required.");

        RuleFor(x => x.Subtype).IsInEnum().When(x => x.Subtype.HasValue)
            .WithMessage("Specified equipment subtype is invalid.");

        RuleFor(x => x)
            .Must(x => CreateEquipmentRequestValidator.HaveValidSubtypeForType(x.Type, x.Subtype))
            .When(x => x.Subtype.HasValue)
            .WithMessage(x => $"EquipmentSubtype '{x.Subtype}' is not valid for EquipmentType '{x.Type}'.")
            .WithName("Subtype");

        RuleFor(x => x.Capacity)
            .GreaterThanOrEqualTo(0).WithMessage("Capacity cannot be negative.")
            .LessThanOrEqualTo(100_000).WithMessage("Capacity exceeds allowable limit.");

        RuleFor(x => x.Capacity)
            .GreaterThan(0).When(x => x.Type != EquipmentType.Other && x.Type != EquipmentType.Sensor)
            .WithMessage("Capacity must be greater than 0.");

        RuleFor(x => x.CurrentVolume)
            .GreaterThanOrEqualTo(0)
            .When(x => x.CurrentVolume.HasValue)
            .WithMessage("Current volume cannot be negative.");

        RuleFor(x => x)
            .Must(x => !x.CurrentVolume.HasValue || x.CurrentVolume.Value <= x.Capacity)
            .When(x => x.CurrentVolume.HasValue && x.Type != EquipmentType.Other && x.Type != EquipmentType.Sensor)
            .WithMessage(x => $"Current volume ({x.CurrentVolume}) cannot exceed equipment capacity ({x.Capacity}).")
            .WithName("CurrentVolume");

        RuleFor(x => x.Unit).IsInEnum().When(x => x.Unit.HasValue).WithMessage("Specified volume unit is invalid.");

        RuleFor(x => x.Description).MaximumLength(500).When(x => !string.IsNullOrEmpty(x.Description))
            .WithMessage("Description cannot exceed 500 characters.");

        RuleFor(x => x.Notes).MaximumLength(2000).When(x => !string.IsNullOrEmpty(x.Notes))
            .WithMessage("Notes cannot exceed 2,000 characters.");

        RuleFor(x => x.BoilOffRatePerHour)
            .InclusiveBetween(0m, 50m)
            .When(x => x.BoilOffRatePerHour.HasValue)
            .WithMessage("Boil-off rate must be between 0 and 50 L/hr.");

        RuleFor(x => x.BoilOffRatePerHour)
            .Must(val => !val.HasValue || val.Value == 0m)
            .When(x => x.Type == EquipmentType.Sensor)
            .WithMessage("Sensors cannot have a boil-off rate.");

        RuleFor(x => x.TrubLossLiters)
            .GreaterThanOrEqualTo(0m)
            .When(x => x.TrubLossLiters.HasValue)
            .WithMessage("Trub loss cannot be negative.");

        RuleFor(x => x.TrubLossLiters)
            .Must(val => !val.HasValue || val.Value == 0m)
            .When(x => x.Type == EquipmentType.Sensor)
            .WithMessage("Sensors cannot have trub loss.");

        RuleFor(x => x.MashTunDeadSpaceLiters)
            .GreaterThanOrEqualTo(0m)
            .When(x => x.MashTunDeadSpaceLiters.HasValue)
            .WithMessage("Mash tun dead space cannot be negative.");

        RuleFor(x => x.MashTunDeadSpaceLiters)
            .Must(val => !val.HasValue || val.Value == 0m)
            .When(x => x.Type == EquipmentType.Sensor)
            .WithMessage("Sensors cannot have mash tun dead space.");

        RuleFor(x => x.PackagingLossLiters)
            .GreaterThanOrEqualTo(0m)
            .When(x => x.PackagingLossLiters.HasValue)
            .WithMessage("Packaging loss cannot be negative.");

        RuleFor(x => x.PackagingLossLiters)
            .Must(val => !val.HasValue || val.Value == 0m)
            .When(x => x.Type == EquipmentType.Sensor)
            .WithMessage("Sensors cannot have packaging loss.");

        RuleFor(x => x.CurrentTemperatureC)
            .InclusiveBetween(-20m, 120m)
            .When(x => x.CurrentTemperatureC.HasValue)
            .WithMessage("Current temperature must be between -20 °C and 120 °C.");

        RuleFor(x => x.ConnectionType)
            .IsInEnum()
            .When(x => x.ConnectionType.HasValue)
            .WithMessage("Specified connection type is invalid.");

        RuleFor(x => x.ConnectionConfigJson)
            .MaximumLength(4000)
            .When(x => !string.IsNullOrEmpty(x.ConnectionConfigJson))
            .WithMessage("Connection configuration cannot exceed 4,000 characters.");
    }
}

public class UpdateVolumeUnitRequestValidator : AbstractValidator<UpdateVolumeUnitRequest>
{
    public UpdateVolumeUnitRequestValidator()
    {
        RuleFor(x => x.VolumeUnit).IsInEnum().WithMessage("A valid volume unit is required.");
    }
}

public class UpdateUserPreferencesRequestValidator : AbstractValidator<UpdateUserPreferencesRequest>
{
    private static readonly string[] ValidLanguages = ["en", "sv"];

    public UpdateUserPreferencesRequestValidator()
    {
        RuleFor(x => x.Language)
            .Must(lang => ValidLanguages.Contains(lang!.ToLower().Trim()))
            .When(x => !string.IsNullOrWhiteSpace(x.Language))
            .WithMessage("Language must be one of the supported codes: 'en', 'sv'.");

        RuleFor(x => x.VolumeUnit)
            .IsInEnum()
            .When(x => x.VolumeUnit.HasValue)
            .WithMessage("Specified volume unit is invalid.");

        RuleFor(x => x.WeightUnit)
            .IsInEnum()
            .When(x => x.WeightUnit.HasValue)
            .WithMessage("Specified weight unit is invalid.");

        RuleFor(x => x.TemperatureUnit)
            .IsInEnum()
            .When(x => x.TemperatureUnit.HasValue)
            .WithMessage("Specified temperature unit is invalid.");

        RuleFor(x => x.GravityUnit)
            .IsInEnum()
            .When(x => x.GravityUnit.HasValue)
            .WithMessage("Specified gravity unit is invalid.");

        RuleFor(x => x.Theme)
            .IsInEnum()
            .When(x => x.Theme.HasValue)
            .WithMessage("Specified theme preference is invalid.");

        RuleFor(x => x.DefaultBatchSizeLiters)
            .InclusiveBetween(1m, 10_000m)
            .When(x => x.DefaultBatchSizeLiters.HasValue)
            .WithMessage("Default batch size must be between 1 and 10,000 liters.");

        RuleFor(x => x.DefaultEfficiencyPercent)
            .InclusiveBetween(10m, 100m)
            .When(x => x.DefaultEfficiencyPercent.HasValue)
            .WithMessage("Default efficiency percent must be between 10% and 100%.");

        RuleFor(x => x.DefaultBoilTimeMinutes)
            .InclusiveBetween(0, 360)
            .When(x => x.DefaultBoilTimeMinutes.HasValue)
            .WithMessage("Default boil time must be between 0 and 360 minutes.");

        RuleFor(x => x.MqttHost)
            .MaximumLength(255)
            .When(x => !string.IsNullOrEmpty(x.MqttHost))
            .WithMessage("MQTT host cannot exceed 255 characters.");

        RuleFor(x => x.MqttPort)
            .InclusiveBetween(1, 65535)
            .When(x => x.MqttPort.HasValue)
            .WithMessage("MQTT port must be between 1 and 65535.");

        RuleFor(x => x.MqttUsername)
            .MaximumLength(100)
            .When(x => !string.IsNullOrEmpty(x.MqttUsername))
            .WithMessage("MQTT username cannot exceed 100 characters.");

        RuleFor(x => x.MqttPassword)
            .MaximumLength(255)
            .When(x => !string.IsNullOrEmpty(x.MqttPassword))
            .WithMessage("MQTT password cannot exceed 255 characters.");

        RuleFor(x => x.MqttCertificate)
            .MaximumLength(16384)
            .When(x => !string.IsNullOrEmpty(x.MqttCertificate))
            .WithMessage("MQTT certificate cannot exceed 16384 characters.");

        RuleFor(x => x.MqttCertificate)
            .Must(cert => cert == null || (cert.Contains("-----BEGIN CERTIFICATE-----") && cert.Contains("-----END CERTIFICATE-----")))
            .When(x => !string.IsNullOrWhiteSpace(x.MqttCertificate))
            .WithMessage("MQTT certificate must be in valid PEM format with -----BEGIN CERTIFICATE----- and -----END CERTIFICATE----- headers.");

        RuleFor(x => x.MqttTopicPrefix)
            .MaximumLength(150)
            .When(x => !string.IsNullOrEmpty(x.MqttTopicPrefix))
            .WithMessage("MQTT topic prefix cannot exceed 150 characters.");

        RuleFor(x => x.MqttTopicPrefix)
            .Must(prefix => prefix == null || (!prefix.Contains('+') && !prefix.Contains('#') && !prefix.Contains('$') && !prefix.Contains('\0')))
            .When(x => !string.IsNullOrEmpty(x.MqttTopicPrefix))
            .WithMessage("MQTT topic prefix cannot contain wildcards ('+', '#') or '$'.");
    }
}

public class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator()
    {
        RuleFor(x => x.DisplayName)
            .NotEmpty().WithMessage("Display name is required.")
            .MaximumLength(100).WithMessage("Display name cannot exceed 100 characters.")
            .Must(name => !name.Contains('<') && !name.Contains('>'))
            .WithMessage("Display name contains invalid characters.");
    }
}

public class CreateBatchRequestValidator : AbstractValidator<CreateBatchRequest>
{
    public CreateBatchRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Batch name is required.")
            .MaximumLength(150).WithMessage("Batch name cannot exceed 150 characters.")
            .Must(name => !name.Contains('<') && !name.Contains('>'))
            .WithMessage("Batch name contains invalid characters.");

        RuleFor(x => x.BatchCode)
            .MaximumLength(50).WithMessage("Batch code cannot exceed 50 characters.")
            .Matches(@"^[a-zA-Z0-9\-_#]+$")
            .When(x => !string.IsNullOrEmpty(x.BatchCode))
            .WithMessage("Batch code contains invalid characters. Use alphanumeric, dash, underscore, or hash.");

        RuleFor(x => x.TargetBatchSizeLiters)
            .InclusiveBetween(0.10m, 100_000.00m)
            .When(x => x.TargetBatchSizeLiters.HasValue)
            .WithMessage("Batch size must be between 0.10 and 100,000 liters.");

        RuleFor(x => x.TargetOg)
            .InclusiveBetween(1.000m, 1.200m)
            .When(x => x.TargetOg.HasValue)
            .WithMessage("Target OG must be between 1.000 and 1.200.");

        RuleFor(x => x.TargetFg)
            .InclusiveBetween(0.980m, 1.150m)
            .When(x => x.TargetFg.HasValue)
            .WithMessage("Target FG must be between 0.980 and 1.150.");

        RuleFor(x => x.MeasuredOg)
            .InclusiveBetween(1.000m, 1.200m)
            .When(x => x.MeasuredOg.HasValue)
            .WithMessage("Measured OG must be between 1.000 and 1.200.");

        RuleFor(x => x.PitchTemperatureC)
            .InclusiveBetween(-10.0m, 110.0m)
            .When(x => x.PitchTemperatureC.HasValue)
            .WithMessage("Pitch temperature must be between -10°C and 110°C.");

        RuleFor(x => x.Notes)
            .MaximumLength(2000)
            .When(x => !string.IsNullOrEmpty(x.Notes))
            .WithMessage("Notes cannot exceed 2000 characters.");

        RuleFor(x => x.BoilOffRatePerHour)
            .InclusiveBetween(0m, 50m)
            .When(x => x.BoilOffRatePerHour.HasValue)
            .WithMessage("Boil-off rate must be between 0 and 50 L/hr.");

        RuleFor(x => x.KettleTrubLossLiters)
            .GreaterThanOrEqualTo(0m)
            .When(x => x.KettleTrubLossLiters.HasValue)
            .WithMessage("Kettle trub loss cannot be negative.");

        RuleFor(x => x.MashTunDeadSpaceLiters)
            .GreaterThanOrEqualTo(0m)
            .When(x => x.MashTunDeadSpaceLiters.HasValue)
            .WithMessage("Mash tun dead space cannot be negative.");

        RuleFor(x => x.FermenterLossLiters)
            .GreaterThanOrEqualTo(0m)
            .When(x => x.FermenterLossLiters.HasValue)
            .WithMessage("Fermenter loss cannot be negative.");

        RuleFor(x => x.PackagingLossLiters)
            .GreaterThanOrEqualTo(0m)
            .When(x => x.PackagingLossLiters.HasValue)
            .WithMessage("Packaging loss cannot be negative.");

        RuleFor(x => x.TargetVolumeBasis)
            .IsInEnum()
            .When(x => x.TargetVolumeBasis.HasValue)
            .WithMessage("Specified target volume basis is invalid.");

        RuleForEach(x => x.CustomIngredients)
            .SetValidator(new BatchIngredientInputDtoValidator())
            .When(x => x.CustomIngredients != null);

        RuleForEach(x => x.CustomMashSteps).SetValidator(new BatchMashStepInputDtoValidator());
        RuleFor(x => x.CustomMashSteps)
            .Must(steps => steps == null || !steps.Any() || steps.Select(s => s.StepOrder).Distinct().Count() == steps.Count)
            .WithMessage("Mash step orders must be unique.");

        RuleForEach(x => x.CustomFermentationSteps).SetValidator(new BatchFermentationStepInputDtoValidator());
        RuleFor(x => x.CustomFermentationSteps)
            .Must(steps => steps == null || !steps.Any() || steps.Select(s => s.StepOrder).Distinct().Count() == steps.Count)
            .WithMessage("Fermentation step orders must be unique.");
    }
}

public class BatchMashStepInputDtoValidator : AbstractValidator<BatchMashStepInputDto>
{
    public BatchMashStepInputDtoValidator()
    {
        RuleFor(x => x.StepOrder)
            .InclusiveBetween(1, 15)
            .WithMessage("Mash step order must be between 1 and 15.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Mash step name is required.")
            .MaximumLength(100).WithMessage("Mash step name cannot exceed 100 characters.")
            .Must(name => !name.Contains('<') && !name.Contains('>'))
            .WithMessage("Mash step name contains invalid characters.");

        RuleFor(x => x.Type)
            .IsInEnum().WithMessage("Invalid mash step type.");

        RuleFor(x => x.TargetTemperatureC)
            .InclusiveBetween(20.0m, 100.0m)
            .WithMessage("Mash step temperature must be between 20°C and 100°C.");

        RuleFor(x => x.DurationMinutes)
            .InclusiveBetween(0, 360)
            .WithMessage("Mash step duration must be between 0 and 360 minutes.");

        RuleFor(x => x.RampTimeMinutes)
            .InclusiveBetween(0, 180)
            .When(x => x.RampTimeMinutes.HasValue)
            .WithMessage("Ramp time must be between 0 and 180 minutes.");

        RuleFor(x => x.InfuseAmountLiters)
            .InclusiveBetween(0.0m, 500.0m)
            .When(x => x.InfuseAmountLiters.HasValue)
            .WithMessage("Infuse amount must be between 0 and 500 liters.");

        RuleFor(x => x.Notes)
            .MaximumLength(1000).WithMessage("Notes cannot exceed 1000 characters.")
            .Must(notes => string.IsNullOrEmpty(notes) || (!notes.Contains('<') && !notes.Contains('>')))
            .WithMessage("Notes contain forbidden characters.");
    }
}

public class BatchFermentationStepInputDtoValidator : AbstractValidator<BatchFermentationStepInputDto>
{
    public BatchFermentationStepInputDtoValidator()
    {
        RuleFor(x => x.StepOrder)
            .InclusiveBetween(1, 20)
            .WithMessage("Fermentation step order must be between 1 and 20.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Fermentation step name is required.")
            .MaximumLength(100).WithMessage("Fermentation step name cannot exceed 100 characters.")
            .Must(name => !name.Contains('<') && !name.Contains('>'))
            .WithMessage("Fermentation step name contains invalid characters.");

        RuleFor(x => x.Type)
            .IsInEnum().WithMessage("Invalid fermentation step type.");

        RuleFor(x => x.TargetTemperatureC)
            .InclusiveBetween(-5.0m, 45.0m)
            .WithMessage("Target temperature must be between -5°C and 45°C.");

        RuleFor(x => x.DurationDays)
            .InclusiveBetween(1, 365)
            .WithMessage("Fermentation step duration must be between 1 and 365 days.");

        RuleFor(x => x.RampTimeHours)
            .InclusiveBetween(0, 168)
            .When(x => x.RampTimeHours.HasValue)
            .WithMessage("Ramp time must be between 0 and 168 hours.");

        RuleFor(x => x.TriggerGravity)
            .InclusiveBetween(0.980m, 1.200m)
            .When(x => x.TriggerGravity.HasValue)
            .WithMessage("Trigger gravity must be between 0.980 and 1.200.");

        RuleFor(x => x.Notes)
            .MaximumLength(1000).WithMessage("Notes cannot exceed 1000 characters.")
            .Must(notes => string.IsNullOrEmpty(notes) || (!notes.Contains('<') && !notes.Contains('>')))
            .WithMessage("Notes contain forbidden characters.");
    }
}

public class ToggleBatchMashStepRequestValidator : AbstractValidator<ToggleBatchMashStepRequest>
{
    public ToggleBatchMashStepRequestValidator()
    {
        RuleFor(x => x.ActualTemperatureC)
            .InclusiveBetween(20.0m, 100.0m)
            .When(x => x.ActualTemperatureC.HasValue)
            .WithMessage("Actual temperature must be between 20°C and 100°C.");

        RuleFor(x => x.ActualDurationMinutes)
            .InclusiveBetween(0, 360)
            .When(x => x.ActualDurationMinutes.HasValue)
            .WithMessage("Actual duration must be between 0 and 360 minutes.");

        RuleFor(x => x.Notes)
            .MaximumLength(1000).WithMessage("Notes cannot exceed 1000 characters.")
            .Must(notes => string.IsNullOrEmpty(notes) || (!notes.Contains('<') && !notes.Contains('>')))
            .WithMessage("Notes contain forbidden characters.");
    }
}

public class ToggleBatchFermentationStepRequestValidator : AbstractValidator<ToggleBatchFermentationStepRequest>
{
    public ToggleBatchFermentationStepRequestValidator()
    {
        RuleFor(x => x.ActualTemperatureC)
            .InclusiveBetween(-20.0m, 120.0m)
            .When(x => x.ActualTemperatureC.HasValue)
            .WithMessage("Actual temperature must be between -20°C and 120°C.");

        RuleFor(x => x.Notes)
            .MaximumLength(1000).WithMessage("Notes cannot exceed 1000 characters.")
            .Must(notes => string.IsNullOrEmpty(notes) || (!notes.Contains('<') && !notes.Contains('>')))
            .WithMessage("Notes contain forbidden characters.");
    }
}

public class LogBatchTemperatureRequestValidator : AbstractValidator<LogBatchTemperatureRequest>
{
    public LogBatchTemperatureRequestValidator()
    {
        RuleFor(x => x.TemperatureC)
            .InclusiveBetween(-20.0m, 120.0m)
            .WithMessage("Temperature must be between -20°C and 120°C.");

        RuleFor(x => x.StepName)
            .MaximumLength(100).WithMessage("Step name cannot exceed 100 characters.")
            .Must(name => string.IsNullOrEmpty(name) || (!name.Contains('<') && !name.Contains('>')))
            .WithMessage("Step name contains forbidden characters.");

        RuleFor(x => x.Notes)
            .MaximumLength(1000).WithMessage("Notes cannot exceed 1000 characters.")
            .Must(notes => string.IsNullOrEmpty(notes) || (!notes.Contains('<') && !notes.Contains('>')))
            .WithMessage("Notes contain forbidden characters.");
    }
}

public class AdvanceBatchStageRequestValidator : AbstractValidator<AdvanceBatchStageRequest>
{
    public AdvanceBatchStageRequestValidator()
    {
        RuleFor(x => x.TargetStage)
            .IsInEnum().WithMessage("A valid target stage is required.");

        RuleFor(x => x.MeasuredOg)
            .InclusiveBetween(1.000m, 1.200m)
            .When(x => x.MeasuredOg.HasValue)
            .WithMessage("Measured OG must be between 1.000 and 1.200.");

        RuleFor(x => x.MeasuredFg)
            .InclusiveBetween(0.980m, 1.150m)
            .When(x => x.MeasuredFg.HasValue)
            .WithMessage("Measured FG must be between 0.980 and 1.150.");

        RuleFor(x => x.MeasuredBatchSizeLiters)
            .InclusiveBetween(0.10m, 100_000.00m)
            .When(x => x.MeasuredBatchSizeLiters.HasValue)
            .WithMessage("Batch volume must be between 0.10 and 100,000 liters.");

        RuleFor(x => x.PitchTemperatureC)
            .InclusiveBetween(-10.0m, 110.0m)
            .When(x => x.PitchTemperatureC.HasValue)
            .WithMessage("Pitch temperature must be between -10°C and 110°C.");

        RuleFor(x => x.Notes)
            .MaximumLength(1000)
            .When(x => !string.IsNullOrEmpty(x.Notes))
            .WithMessage("Notes cannot exceed 1000 characters.");

        RuleFor(x => x.MeasuredPreBoilVolumeLiters)
            .GreaterThan(0m)
            .When(x => x.MeasuredPreBoilVolumeLiters.HasValue)
            .WithMessage("Pre-boil volume must be positive.");

        RuleFor(x => x.MeasuredPreBoilGravity)
            .InclusiveBetween(1.000m, 1.200m)
            .When(x => x.MeasuredPreBoilGravity.HasValue)
            .WithMessage("Pre-boil gravity must be between 1.000 and 1.200.");

        RuleFor(x => x.MeasuredPostBoilVolumeLiters)
            .GreaterThan(0m)
            .When(x => x.MeasuredPostBoilVolumeLiters.HasValue)
            .WithMessage("Post-boil volume must be positive.");

        RuleFor(x => x.MeasuredPackagedVolumeLiters)
            .GreaterThan(0m)
            .When(x => x.MeasuredPackagedVolumeLiters.HasValue)
            .WithMessage("Packaged volume must be positive.");
    }
}

public class AddBatchReadingRequestValidator : AbstractValidator<AddBatchReadingRequest>
{
    public AddBatchReadingRequestValidator()
    {
        RuleFor(x => x.SpecificGravity)
            .InclusiveBetween(0.980m, 1.200m)
            .WithMessage("Specific gravity must be between 0.980 and 1.200.");

        RuleFor(x => x.TemperatureC)
            .InclusiveBetween(-10.0m, 110.0m)
            .When(x => x.TemperatureC.HasValue)
            .WithMessage("Temperature must be between -10°C and 110°C.");

        RuleFor(x => x.Notes)
            .MaximumLength(500)
            .When(x => !string.IsNullOrEmpty(x.Notes))
            .WithMessage("Reading notes cannot exceed 500 characters.");
    }
}

public class UpdateBatchRequestValidator : AbstractValidator<UpdateBatchRequest>
{
    public UpdateBatchRequestValidator()
    {
        RuleFor(x => x.Name)
            .MaximumLength(150)
            .Must(name => name == null || (!name.Contains('<') && !name.Contains('>')))
            .When(x => !string.IsNullOrEmpty(x.Name))
            .WithMessage("Batch name contains invalid characters.");

        RuleFor(x => x.BeerStyle)
            .MaximumLength(100)
            .Must(style => style == null || (!style.Contains('<') && !style.Contains('>')))
            .When(x => !string.IsNullOrEmpty(x.BeerStyle))
            .WithMessage("Beer style contains invalid characters.");

        RuleFor(x => x.Notes)
            .MaximumLength(2000)
            .Must(notes => notes == null || (!notes.Contains('<') && !notes.Contains('>')))
            .When(x => !string.IsNullOrEmpty(x.Notes))
            .WithMessage("Notes cannot exceed 2000 characters or contain invalid characters.");

        RuleFor(x => x.MeasuredOg)
            .InclusiveBetween(1.000m, 1.200m)
            .When(x => x.MeasuredOg.HasValue)
            .WithMessage("Measured OG must be between 1.000 and 1.200.");

        RuleFor(x => x.MeasuredFg)
            .InclusiveBetween(0.980m, 1.150m)
            .When(x => x.MeasuredFg.HasValue)
            .WithMessage("Measured FG must be between 0.980 and 1.150.");

        RuleFor(x => x.PitchTemperatureC)
            .InclusiveBetween(-10.0m, 110.0m)
            .When(x => x.PitchTemperatureC.HasValue)
            .WithMessage("Pitch temperature must be between -10°C and 110°C.");

        RuleFor(x => x.MeasuredBatchSizeLiters)
            .InclusiveBetween(0.10m, 100_000.00m)
            .When(x => x.MeasuredBatchSizeLiters.HasValue)
            .WithMessage("Batch volume must be between 0.10 and 100,000 liters.");

        RuleFor(x => x.MeasuredPreBoilVolumeLiters)
            .GreaterThan(0m)
            .When(x => x.MeasuredPreBoilVolumeLiters.HasValue)
            .WithMessage("Pre-boil volume must be positive.");

        RuleFor(x => x.MeasuredPreBoilGravity)
            .InclusiveBetween(1.000m, 1.200m)
            .When(x => x.MeasuredPreBoilGravity.HasValue)
            .WithMessage("Pre-boil gravity must be between 1.000 and 1.200.");

        RuleFor(x => x.MeasuredPostBoilVolumeLiters)
            .GreaterThan(0m)
            .When(x => x.MeasuredPostBoilVolumeLiters.HasValue)
            .WithMessage("Post-boil volume must be positive.");

        RuleFor(x => x.MeasuredPackagedVolumeLiters)
            .GreaterThan(0m)
            .When(x => x.MeasuredPackagedVolumeLiters.HasValue)
            .WithMessage("Packaged volume must be positive.");
    }
}

public class CalculateWaterVolumeRequestValidator : AbstractValidator<CalculateWaterVolumeRequest>
{
    public CalculateWaterVolumeRequestValidator()
    {
        RuleFor(x => x.BatchSizeLiters).GreaterThan(0m).WithMessage("Batch size must be positive.");
        RuleFor(x => x.BoilTimeMinutes).GreaterThanOrEqualTo(0).WithMessage("Boil time cannot be negative.");
        RuleFor(x => x.TotalGrainWeightKg).GreaterThanOrEqualTo(0m).WithMessage("Grain weight cannot be negative.");
        RuleFor(x => x.BoilOffRatePerHourLiters).InclusiveBetween(0m, 50m).When(x => x.BoilOffRatePerHourLiters.HasValue);
        RuleFor(x => x.GrainAbsorptionRateLPerKg).InclusiveBetween(0.1m, 3.0m).When(x => x.GrainAbsorptionRateLPerKg.HasValue);
        RuleFor(x => x.CoolingShrinkagePercent).InclusiveBetween(0m, 20m).When(x => x.CoolingShrinkagePercent.HasValue);
    }
}