using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace ADF435x
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]


        static void Main(string[] args)
        {
            string ADIsimPLL_import_file = "";
            int i = 0;
            foreach (string s in args)
                ADIsimPLL_import_file += args[i++] + " ";

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Main_Form(ADIsimPLL_import_file));
        }
    }
}
