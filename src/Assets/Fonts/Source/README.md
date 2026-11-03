# Archivo source

`Archivo-Variable.ttf` is the upstream variable font. Avalonia cannot select weights inside a
variable font, so `../Archivo-Regular.ttf` and `../Archivo-Bold.ttf` are static instances of it
(fontTools `varLib.instancer`, `wght` 400 and 700, `wdth` 100), matching the Regular and Bold
named instances Qt used.
