﻿// ##############################################################################
// # This code implements an interface to a Tecalor/Stiebel Eltron Heatpump using
// # the CAN bus Interfaces supported USBtin
// #
// # I used a lot of the hard work of Jürg http://juerg5524.ch/ and from Immi (THZ module)
// # and from Radiator (Hartmut Schmidt) . 
// # The source code is based on Radiator's FHEM module 00_WPL15.pm 0.15 v. 12.05.2016
// # The Elster Codes are valid for a WPL/TTL AC 10  Heatpump with WPM 3.
// # Other pumps may have other codes.
// # by Bytehunter
// # !! Do not use the set function to write to the pump unless you know what you are doing.
// # !! Improper usage of "set" might damage the pump
// ##############################################################################
// 
//  This programm is free software; you can redistribute it and/or modify
//  it under the terms of the GNU General Public License as published by
//  the Free Software Foundation; either version 2 of the License, or
//  (at your option) any later version.
// 
//  The GNU General Public License can be found at
//  http://www.gnu.org/copyleft/gpl.html.
//  A copy is found in the textfile GPL.txt and important notices to the license
//  from the author is found in LICENSE.txt distributed with these scripts.
// 
//  This script is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//  GNU General Public License for more details.
// ##############################################################################
using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Systemd;
using Microsoft.Extensions.Logging;

namespace AC10Service;


class Program
{
    static async Task Main(string[] args)
    {
        HostBuilder builder = new HostBuilder();


        builder
            .UseSystemd() // Dieser Aufruf registriert den Hosted Service als Linux-Service;
            .ConfigureLogging((hostingContext, logging) =>
            {
                logging.AddConfiguration(hostingContext.Configuration.GetSection("Logging"));
                if (SystemdHelpers.IsSystemdService())
                {
                    logging.AddSystemdConsole(options =>
                    {
                        options.IncludeScopes = true;
                    });
                }
                else
                {
                    logging.AddConsole();
                }                   
            })
            .ConfigureAppConfiguration((hostingContext, config) =>
            {
                config.AddEnvironmentVariables();
                if (args != null)
                {
                    config.AddCommandLine(args);
                }
                config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
            })
            .ConfigureServices((hostContext, services) =>
            {
                services.AddOptions();
                services.Configure<UsbTinCanBusAdapterConfig>(hostContext.Configuration.GetSection("UsbTinCanBusAdapterConfig"));
                services.Configure<AC10MqttAdapterConfig>(hostContext.Configuration.GetSection("AC10MqttAdapterConfig"));
                services.Configure<AC10HeatingAdapterConfig>(hostContext.Configuration.GetSection("AC10HeatingAdapterConfig"));
                services.AddSingleton<AC10MqttAdapter>();
                services.AddSingleton<UsbTinCanBusAdapter>();
                services.AddHostedService<AC10HeatingMqttService>();
            });

        
        await builder.RunConsoleAsync();
    }
}