﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Runtime.InteropServices;
using Excel = Microsoft.Office.Interop.Excel;

namespace LASAnalysis
{
    class SpreadSheetChecker : IDisposable
    {
        public SpreadSheetChecker()
        {
            // Initiate the Excel application for checking.
            this.excelApp = new Excel.Application();

            // Initiate the result buffers.
            this.summaryBuilder = new StringBuilder();
            this.detailedResultBuilder = new StringBuilder();

            // Add headers for output files.
            summaryBuilder.Append("Filename,No. of Incorrect Input" + System.Environment.NewLine);
            detailedResultBuilder.Append("Filename,Incorrect Cell,Given Input,Correct Answer" + System.Environment.NewLine);

            // Load the answer keys.
            LoadAnswerKey();

            // Make sure to have the directory created, if it does not exist.
            Directory.CreateDirectory(Path.GetDirectoryName(_summaryOutputFile));

            Console.WriteLine("Fetching refracted light from Saturn's rings, and analyzing some spreadsheets...");

            // Do the actual correctness checking.
            CheckInputCorrectness();

            Console.WriteLine("Done with the spreadsheet analysis. No update from Saturn though.");
        }

        // Returns list of all .xlsm files' absolute path for the input directory.
        private IEnumerable<string> GetAllExcelMacroFiles()
        {
            string supportedFiletypes = "*.xls,*.xlsx,*.xlsm";
            //TODO: move the directory input into a config file.
            return Directory.GetFiles(@"C:\Data\LAS RQ34 zip code submissions\", "*.*", 
                                        SearchOption.AllDirectories).Where(s => 
                                        supportedFiletypes.Contains(Path.GetExtension(s).ToLower()));       
        }

        // Loads the answer key from the answer file into a dictionary as <cell_location, value> pairs.
        private void LoadAnswerKey()
        {
            // Get the range C2:C62 
            answerMap = new Dictionary<string, string>();
            GetCellRangeValues(@"C:\Data\LAS_Answer_Key\Zipcode task key.xls", "C", 2, 62, answerMap);
        }

        // Workhorse program, goes over every single file, opens them and checks 
        // the desired range with the answer key we loaded.
        private void CheckInputCorrectness()
        {
            Dictionary<string, string> rangeMap = new Dictionary<string, string>();
            int filesWithIncorrectInputCount = 0;
            foreach (string inputFile in GetAllExcelMacroFiles())
            {
                // Get the required range.
                GetCellRangeValues(inputFile, "C", 2, 62, rangeMap);

                int incorrectCount = 0;
                Dictionary<string, string> incorrectInputMap = new Dictionary<string, string>();
                foreach (KeyValuePair<string,string> entry in answerMap)
                {
                    string answer = entry.Value.ToString().Trim();
                    string input;
                    if (!rangeMap.TryGetValue(entry.Key, out input))
                    {
                        // Input does not have this cell. No further processing needed, move to next cell.
                        incorrectCount++;
                        continue;
                    }

                    if (!answer.Equals(input.Trim()))
                    {
                        incorrectCount++;
                        incorrectInputMap.Add(entry.Key, input);
                    }
                }

                // Process the result if there is any incorrect input.
                if (incorrectCount > 0)
                {
                    filesWithIncorrectInputCount++;
                    ProcessCorrectnessResult(inputFile, incorrectCount, incorrectInputMap);
                }                
            }

            // Add the total no. of incorrect files to summary.
            summaryBuilder.Append("Total files with incorrect input: " + filesWithIncorrectInputCount.ToString()
                                    + Environment.NewLine);

            // Write out the result to file.
            File.WriteAllText(_summaryOutputFile, summaryBuilder.ToString());
            File.WriteAllText(_detailedResultOutputFile, detailedResultBuilder.ToString());
        }

        // Processes the correctness result of an input file. Given are the file path, no. of incorrect cells
        // and the cells which had the incorrect value along with that incorrect value.
        private void ProcessCorrectnessResult(string filePath, int incorrectCount, Dictionary<string, string> incorrectInputMap)
        {
            // Add to the summary.
            summaryBuilder.Append(filePath + "," + incorrectCount.ToString() + Environment.NewLine);

            // Add the incorrect cell with the input and the correct answer.
            foreach (KeyValuePair<string, string> incorrectInput in incorrectInputMap)
            {
                detailedResultBuilder.Append(filePath + "," + incorrectInput.Key.ToString() + ","
                                            + incorrectInput.Value.ToString() + "," 
                                            + answerMap[incorrectInput.Key.ToString()] + Environment.NewLine);
            }            
        }

        private void GetCellRangeValues(string filePath, string column, int rangeStart, int rangeEnd, Dictionary<string, string> rangeMap)
        {
            Excel.Workbook workBook = null;
            Excel.Worksheet sheet = null;
            
            // Clear out the dictionary.
            rangeMap.Clear();

            try
            {
                if (this.excelApp != null)
                {
                    // Load the answer key file.
                    workBook = this.excelApp.Workbooks.Open(filePath);

                    // Get the active sheet, there should be only one (as expected).
                    sheet = (Excel.Worksheet)workBook.ActiveSheet;
                    
                    for (int cellIndex = rangeStart; cellIndex <= rangeEnd; cellIndex++)
                    {
                        string cellLocation = column + cellIndex.ToString();
                        Excel.Range keyCell = (Excel.Range)sheet.Range[cellLocation];
                        Object keyCellValue = keyCell.Value2;
                        if (keyCellValue != null)
                        {
                            rangeMap.Add(cellLocation, keyCellValue.ToString());
                        }
                        else
                        {
                            rangeMap.Add(cellLocation, "");
                        }                        
                    }
                }
            }
            catch (Exception e)
            {
                // Dump the message on console.
                Console.WriteLine(e.Message);

                // Write the actual exception message to log file.
                File.AppendAllText(_errorFile, e.ToString() + Environment.NewLine);
            }
            finally
            {
                // Close the workbook without saving anything.
                if (workBook != null)
                {
                    workBook.Close(false);
                }
                
                if (sheet != null)
                {
                    Marshal.ReleaseComObject(sheet);
                }

                if (workBook != null)
                {
                    Marshal.ReleaseComObject(workBook);
                }
            }
        }

        public void Dispose()
        {
            // Quit the Excel application and release it.
            if (excelApp != null)
            {
                excelApp.Quit();
            }

            Marshal.ReleaseComObject(excelApp);
        }

        // Result file paths, gets overwritten every time program is run.
        // TODO: Move them out to config.
        private static readonly string _summaryOutputFile = @"..\..\..\output\Summary.csv";
        private static readonly string _detailedResultOutputFile = @"..\..\..\output\Detailed Result.csv";

        // Error file.
        private static readonly string _errorFile = @"..\..\..\output\ErrorLog.txt";

        // Map of answers with cells to value, eg. <Col#, 'value'> format.
        private Dictionary<string, string> answerMap;

        // Single Excel interop app, so that we don't keep on opening stuff.
        private Excel.Application excelApp;

        // Buffers that holds the results in place until dumped on disk.
        private StringBuilder summaryBuilder;
        private StringBuilder detailedResultBuilder;
    }
}