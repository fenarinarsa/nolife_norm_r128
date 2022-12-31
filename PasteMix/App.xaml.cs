using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace PasteMix
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public static String mixok_dir = @"Z:\Médias centralisés\Mix\MIX OK";
        public static String configfile = "config_pastemix.txt";
        public static String output_dir = "";
    }
}
