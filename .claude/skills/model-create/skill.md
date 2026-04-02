---
name: model-create
description: Create a new entity model, update AppDbContext, and generate migration. Provide model name and properties as input.
allowed-tools: Read, Write, Edit, Bash, AskUserQuestion
---

# Model Create

## Instructions

Create a new entity model with audit and soft-delete support, register it in DbContext, and generate an EF Core migration.

### Steps

1. **Gather Model Information**
   - Ask user for:
     - Model name (e.g., `Product`, `Order`)
     - Properties in format and required format: `PropertyName:Type` (e.g., `Name:string[MaxLength:9]`, `Price:decimal`, `Quantity:int`)
     - Confirm use of IAuditable and ISoftDelete (default: no)

2. **Create Entity File**
   - Create `Fiolyn/Entities/{ModelName}.cs`
   - Implement `IAuditable` and `ISoftDelete` interfaces (in need)
   - Add properties with `[Required]` annotations where appropriate
   - Use primary constructor pattern per CLAUDE.md
   - Add XML documentation

3. **Update AppDbContext**
   - Read `Fiolyn/Data/AppDbContext.cs`
   - Add `public DbSet<{ModelName}> {ModelNamePlural} => Set<{ModelName}>();`
   - Keep alphabetical order

4. **Create Migration**
   - Run: `dotnet ef migrations add Add{ModelName} --output-dir Data/Migrations`
   - Change to `Fiolyn/` directory first

5. **Confirm and Report**
   - Show user the created entity file
   - Show the DbContext change
   - Report migration name
   - Ask: *"Review the changes and apply migration? (yes / no)"*
   - If yes, run: `dotnet ef database update`

---

## Entity File Template

```csharp
using System.ComponentModel.DataAnnotations;
using Fiolyn.Entities.Base;

namespace Fiolyn.Entities;

/// <summary>
///   Represents a {ModelDescription}.
/// </summary>
public class {ModelName} : IAuditable, ISoftDelete
{
  [Key]
  public long Id { get; set; }

  // Properties here

  [Required]
  public DateTime Created { get; set; }

  [Required]
  public DateTime LastModified { get; set; }

  [Required]
  public bool IsDeleted { get; set; }
}
```

---

## Constraints

- Model name must be a valid C# identifier
- Properties must include type (string, int, decimal, DateTime, etc.)
- Entity always includes IAuditable and ISoftDelete
- Always use 2-space indentation
- Always add XML documentation
- Run commands from Fiolyn/ subdirectory
