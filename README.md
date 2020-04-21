BcrWriter
=========

Library for writing GPS data files according to ISO 25178-7, ISO 25178-71 and EUNA 15178. 

## Usage
1) instantiate class;
2) provide required properties;
3) provide topography data as array by calling `PrepareMainSection(double[])` or `PrepareMainSection(int[])`;
4) optionally produce a file trailer by calling `PrepareTrailerSection(Dictonary<string, string>)`;
5) finally produce the output file by calling `WriteToFile(string)`.

## Caveat
* `PrepareMainSection(double[])` multiplies the z-data with `ZScale` (assuming data is in m).
* `PrepareMainSection(int[])` uses z-data unmodified (assuming data is in µm) and sets `ZScale` to 1e-6.
* Most properties must be set in advance, otherwise no output will be generated.
* `NumberOfPointsPerProfile` and `NumberOfProfiles` must not be modified during operation

### Property Relaxed
If `Relaxed` is set to true following non standard formats apply:
* field dimensions can be larger than 65535;
* the `ManufacID` may be longer than 10 characters;
* invalid data points are coded by `NaN` instead of `BAD`.
