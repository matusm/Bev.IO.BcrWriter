//*******************************************************************************************
//
// Library for writing GPS data files according to ISO 25178-7, ISO 25178-71 and EUNA 15178. 
//
// Usage:
//   1) instantiate class;
//   2) provide required properties;
//   3) provide topography data by calling PrepareMainSection(double[] or int[]);
//   4) optionally produce a file trailer by calling PrepareTrailerSection(Dictonary<string, string>);
//   5) finally produce the output file by calling WriteToFile(string).
//
// Caveat:
//   PrepareMainSection(double[]) multiplies the z-data with 1e6 (assuming data is in m)
//   PrepareMainSection(int[]) uses z-data unmodified (assuming data is in µm)
//
// Known problems and restrictions:
//   most properties must be set in advance, otherwise no output will be generated
//   NumberOfPointsPerProfile and NumberOfProfiles must not be modified during operation
//
// Author: Michael Matus, 2017
//   1.3.3  empty trailer ends with the delimiter "*", 2020
//   1.3.0  WriteToFile() returns bool, 2020
//   1.2.0  property "Relaxed" added
//   1.1.0	support for int data, 2020
//   1.0.1	refactored source code, 2020
//   1.0.0	first working version, 2017
//
//*******************************************************************************************


using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace Bev.IO.BcrWriter
{
    public class BcrWriter
    {

        #region Properties

        // If true use ISO 25178-71 format, if false use the legacy BCR format.
        public bool ForceIsoFormat { get; set; }

        // If true, allow >65535 points per profile and profiles
        public bool Relaxed { get; set; }

        // If true forces the ".SDF" extension for the output file name.
        public bool ForceDefaultFileExtension { get; set; }

        // The instrument identifier. Mandatory for file header.
        public string ManufacurerId { get; set; }

        // The scan's creation date. Mandatory for file header.
        public DateTime CreationDate { get; set; }

        // The scan's modification date. Mandatory for file header.
        public DateTime ModificationDate { get; set; }

        // The number of points per profile. Mandatory for file header.
        public int NumberOfPointsPerProfile { get; set; }

        // The number of profiles. Mandatory for file header.
        public int NumberOfProfiles { get; set; }

        // The point spacing in m. Mandatory for file header.
        public double XScale { get; set; }

        // The profile spacing in m. Mandatory for file header.
        public double YScale { get; set; }

        #endregion

        #region Ctor
        // Creates an instance of the BcrWriter class.
        // Sets some properties to default values.
        public BcrWriter()
        {
            // I am not completely confident on this, but it works.
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            ForceDefaultFileExtension = true;
            ForceIsoFormat = false;
            zDataType = ZDataType.None;
            ModificationDate = DateTime.UtcNow;
        }
        #endregion

        #region Public Methods

        // Writes the formatted data (if present) to a text file.
        // returns true if successful, false otherwise 
        public bool WriteToFile(string outFileName)
        {
            // check if data present
            if (string.IsNullOrWhiteSpace(DataToString()))
                return false;
            // change file name extension
            string fileName = outFileName;
            if(ForceDefaultFileExtension)
                fileName = Path.ChangeExtension(outFileName, ".sdf");
            // write the file
            try
            {
                StreamWriter hOutFile = File.CreateText(fileName);
                hOutFile.Write(DataToString());
                hOutFile.Close();
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }

        // Returns the formated data (if present) as string.
        public string DataToString()
        {
            // check if data is already prepared
            if (headerSectionSb == null)
                return "";
            if (dataSectionSb == null)
                return "";
            string returnString = headerSectionSb.ToString() + dataSectionSb.ToString() + trailerSectionSb.ToString();
            return returnString;
        }

        // Prepares the header and data section. 
        // "Record 1" and "Record 2" according to ISO 25178-71
        // The topography data in units of meter.
        public void PrepareMainSection(double[] topographyData)
        {
            zDataType = ZDataType.Double;
            // perform some validity checks
            if (topographyData == null)
                return;
            if (topographyData.Length != NumberOfPointsPerProfile * NumberOfProfiles)
                return;
            // prepare the header section
            PrepareHeaderSection();
            // create the StringBuilder for the data section
            dataSectionSb = new StringBuilder();
            // write the scan data in µm
            foreach (double z in topographyData)
            {
                if (double.IsNaN(z))
                {
                    dataSectionSb.AppendLine("BAD"); // ISO 25178-71 clause 5.3.2
                }
                else
                {
                    dataSectionSb.AppendLine($"{z*1e6:F6}"); // fixed resolution of 1 pm
                }
            }
            // End of section delimiter
            dataSectionSb.AppendLine("*");
        }

        // Prepares the header and data section. 
        // "Record 1" and "Record 2" according to ISO 25178-71
        // The topography data in units of micrometer.
        public void PrepareMainSection(int[] topographyData)
        {
            zDataType = ZDataType.Int32;
            // perform some validity checks
            if (topographyData == null)
                return;
            if (topographyData.Length != NumberOfPointsPerProfile * NumberOfProfiles)
                return;
            // prepare the header section
            PrepareHeaderSection();
            // create the StringBuilder for the data section
            dataSectionSb = new StringBuilder();
            // write the scan data in µm
            foreach (double z in topographyData)
            {
                dataSectionSb.AppendLine($"{z}");
            }
            // End of section delimiter
            dataSectionSb.AppendLine("*");
        }

        /// Prepares the (optional) trailer section from a given dictionary.
        /// "Record 3" according to ISO 25178-71
        public void PrepareTrailerSection(Dictionary<string, string> trailerEntries)
        {
            // create the StringBuilder for the file trailer section
            trailerSectionSb = new StringBuilder();
            // perform some validity checks
            if (trailerEntries == null)
            {
                trailerSectionSb.AppendLine("*");
                return;
            }
            // padd all keys to the same length
            Dictionary<string, string> niceEntries = BeautifyKeyStrings(trailerEntries);
            // iterate the dictonary
            foreach (string k in niceEntries.Keys)
                trailerSectionSb.AppendLine(BcrTrailerLine(k, niceEntries[k]));
            // End of section delimiter
            trailerSectionSb.AppendLine("*");
        }

        #endregion

        #region Private Methods

        // Prepares the SDF file header section. Respective properties must be set in advance.
        // Constitutes "Record 1" according to ISO 25178-71
        private void PrepareHeaderSection()
        {
            // for single profiles YScale must be 0
            if (NumberOfProfiles == 1)
                YScale = 0;
            if (!Relaxed)
            {
                // the SDF definition suffers from the historical restriction of 65535 points per profile 
                // there is no error message up to now, it is just not possible to create data.
                if (NumberOfPointsPerProfile > ushort.MaxValue)
                    return;
                if (NumberOfProfiles > ushort.MaxValue)
                    return;
            }
            // instantiate the StringBuilder for the header section
            headerSectionSb = new StringBuilder();
            if (ForceIsoFormat)
            {
                headerSectionSb.AppendLine("aISO-1.0");
            }
            else
            {
                headerSectionSb.AppendLine("aBCR-1.0");
            }
            headerSectionSb.AppendLine($"ManufacID   = {BcrEncode(ManufacurerId)}"); // just tu make sure no "=" is added
            headerSectionSb.AppendLine($"CreateDate  = {CreationDate.ToString("ddMMyyyyHHmm")}");
            headerSectionSb.AppendLine($"ModDate     = {ModificationDate.ToString("ddMMyyyyHHmm")}");
            headerSectionSb.AppendLine($"NumPoints   = {NumberOfPointsPerProfile}");
            headerSectionSb.AppendLine($"NumProfiles = {NumberOfProfiles}");
            headerSectionSb.AppendLine($"Xscale      = {XScale.ToString("G5")}");
            headerSectionSb.AppendLine($"Yscale      = {YScale.ToString("G5")}");
            headerSectionSb.AppendLine( "Zscale      = 1E-06"); // always use µm
            headerSectionSb.AppendLine( "Zresolution = -1"); // clause 5.2.8, do not modify!
            headerSectionSb.AppendLine( "Compression = 0"); // clause 5.2.9, do not modify!
            switch (zDataType)
            {
                case ZDataType.Double:
                    headerSectionSb.AppendLine("DataType    = 7"); 
                    break;
                case ZDataType.Int32:
                    headerSectionSb.AppendLine("DataType    = 6"); 
                    break;
                case ZDataType.Int16:
                    headerSectionSb.AppendLine("DataType    = 5"); 
                    break;
                default:
                    headerSectionSb.AppendLine("DataType    = ?"); // this should not happen!
                    break;
            }
            headerSectionSb.AppendLine("CheckType   = 0"); // clause 5.2.11, do not modify!
            headerSectionSb.AppendLine("*");
        }

        // Formats a given directory by trimming the values (strings) and padding the keys to the maximum length of all keys (strings).
        private Dictionary<string, string> BeautifyKeyStrings(Dictionary<string, string> rawDictonary)
        {
            // determine the length of the longest (trimmed) key
            int maxKeyLength = 0;
            foreach (string k in rawDictonary.Keys)
                if (k.Trim().Length > maxKeyLength) maxKeyLength = k.Trim().Length;
            // build the beautified dictonary
            Dictionary<string, string> processedDictonary = new Dictionary<string, string>();
            foreach (string k in rawDictonary.Keys)
                processedDictonary[BcrEncode(k.Trim().PadRight(maxKeyLength))] = BcrEncode(rawDictonary[k].Trim());
            return processedDictonary;
        }

        // Prepares a line for the file trailer in on of two possible formats.
        private string BcrTrailerLine(string key, string value)
        {
            return BcrTrailerLine(key, value, "");
        }

        // Prepares a line for the file trailer in on of two possible formats.
        // A comment can be included also.
        // Any information following a ";" is considered as a comment according to section 8.3 of ISO 25178-7.
        // This behaviour is not defined in ISO 25178-71 however, so comments are suppressed in this case.
        // anyway this method is private, therefore probably obsolete
        private string BcrTrailerLine(string key, string value, string comment)
        {
            string returnString;
            if(ForceIsoFormat)
                returnString = $"<{key.Trim()}> {value.Trim()} </{key.Trim()}>";
            else
                returnString = $"{key.TrimStart()} = {value.Trim()}";
            // append a comment if appropiate
            if (!string.IsNullOrWhiteSpace(comment) && !ForceIsoFormat)
                returnString += $" ; {comment.Trim()}";
            return returnString;
        }

        // encodes a string to be safely used in the header and trailer sections
        // replaces =, >, <, * with safe characters
        // take care that the length of the string is not changed 
        private string BcrEncode(string rawStr)
        {
            string encStr = rawStr.Replace(@"=", @":");
            encStr = encStr.Replace(@"<", @"[");
            encStr = encStr.Replace(@">", @"]");
            encStr = encStr.Replace(@"\", @"|");
            encStr = encStr.Replace(@"*", @"#");
            return encStr;
        }

        #endregion

        #region Private Fields

        // this field is to control the "DataType" in the header
        ZDataType zDataType;
        // The whole file contents (three sections) is stored in three StringBuilders.
        private StringBuilder headerSectionSb;
        private StringBuilder dataSectionSb;
        private StringBuilder trailerSectionSb;

        #endregion

    }

    // Allowed data types, ISO 25178-71 clause 5.2.10
    enum ZDataType
    {
        None,
        Int16, // short, not implemented
        Int32, // int
        Double // double
    }
}
