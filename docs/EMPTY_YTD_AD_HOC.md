# Empty YTD support — AD HOC workaround

This is a temporary HikariHopper compatibility workaround, **not native empty-YTD
support in FiveFury Python 0.5.1**. That version rejects empty dictionaries during
validation/writing and rejects the null item-table pointer when reading them.

`ytd_empty_ad_hoc.py` contains two synthetic 55-byte templates: GTA V Legacy
(resource version 13) and Enhanced (version 5). Both contain zero textures, zero
array counts/capacities, null array pointers and no graphics data. There is no
placeholder texture. The edition comes from the explicit creation target or the
loaded document, never from the folder to which it was moved.
Creating a YTD in a standalone RPF without a configured game context is rejected
with an explicit error: an RPF by itself does not identify the intended YTD edition.

## Scope

- Creation and saving an empty model return the corresponding template.
- Opening recognizes only its exact header and decompressed resource contents;
  equivalent recompression by RPF storage is accepted. Other files go through the
  existing FiveFury reader, without catching errors and inventing an empty model.
- Removing the last texture or all selected textures is allowed and undoable.
  Save and Save as remain available for an empty loaded document.
- Nonempty documents retain normal validation and the existing page-layout fix.
- This does not implement a general reader for every possible empty YTD layout,
  nor add texture import to the editor. It requires no .NET runtime on user PCs.

## Provenance and regeneration

Generated and strictly read/validated with FiveFury.NET at commit
`78dd78edd50298e1440a310191c095242d90f4b0`, using its public API:

```csharp
foreach (var target in new[] { GameTarget.Legacy, GameTarget.Enhanced })
{
    byte[] bytes = new Ytd(target).Write();
    Ytd parsed = Ytd.Read(bytes);
    parsed.Validate().ThrowIfInvalid();
    if (parsed.Target != target || parsed.Textures.Count != 0)
        throw new InvalidDataException("Empty YTD round-trip changed its model.");
    Console.WriteLine(Convert.ToHexString(bytes));
}
```

These are newly generated resources, not assets copied from a game. .NET strict
validation and HikariHopper round trips do not constitute in-game testing.
The generating/validating assembly had SHA-256
`59f676cb431d8a06a6ae5af287a27d006a6c97b0c770ea8660bd00b88979ddab`.

## Removal criterion

Delete this module and its empty-only branches when the app's FiveFury integration
supports zero-texture read, validation and writing natively for **both** editions.
Prefer the FiveFury.NET integration; do not grow a second serializer here. Keep
the creation, save, RPF, selection and undo regression tests when removing it.
