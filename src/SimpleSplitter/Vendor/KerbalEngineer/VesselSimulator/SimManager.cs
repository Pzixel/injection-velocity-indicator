#nullable disable
// Vendored from KER; local changes documented in README.vendor.md.
// 
//     Kerbal Engineer Redux
// 
//     Copyright (C) 2014 CYBUTEK
// 
//     This program is free software: you can redistribute it and/or modify
//     it under the terms of the GNU General Public License as published by
//     the Free Software Foundation, either version 3 of the License, or
//     (at your option) any later version.
// 
//     This program is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY; without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU General Public License for more details.
// 
//     You should have received a copy of the GNU General Public License
//     along with this program.  If not, see <http://www.gnu.org/licenses/>.
// 

namespace SimpleSplitter.Engineer.VesselSimulator
{

    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Reflection;
    using System.Threading;
    using UnityEngine;
    using Helpers;

    public class SimManager
    {

        public const double RESOURCE_MIN = 0.0001;
        public const double RESOURCE_PART_EMPTY_THRESH = 0.01;
        public static LogMsg log = new LogMsg();

        // Support for RealFuels using reflection to check localCorrectThrust without dependency

        private static bool hasCheckedForMods;
        public static bool hasInstalledRealFuels;
        private static FieldInfo RF_ModuleEngineConfigs_localCorrectThrust;
        private static FieldInfo RF_ModuleHybridEngine_localCorrectThrust;
        private static FieldInfo RF_ModuleHybridEngines_localCorrectThrust;
        private static bool hasInstalledKIDS;
        private static MethodInfo KIDS_Utils_GetIspMultiplier;
        private static bool bKIDSThrustISP = false;
        private static object[] KIDSparameters;

        private static bool useRFResiduals = false;
        public static bool RFResiduals
        {
            get
            {
                return useRFResiduals;
            }
            set
            {
                if (hasInstalledRealFuels)
                    useRFResiduals = value;
                else
                    useRFResiduals = false;
            }
        }

        public static String failMessage { get; private set; }

        private static void CheckForMods()
        {
            hasCheckedForMods = true;

            foreach (var assembly in AssemblyLoader.loadedAssemblies)
            {
                log.AppendLine("Assembly: ", assembly.assembly);

                var name = assembly.assembly.ToString().Split(',')[0];

                if (name == "RealFuels")
                {
                    log.AppendLine("Found RealFuels mod");

                    var RF_ModuleEngineConfigs_Type = assembly.assembly.GetType("RealFuels.ModuleEngineConfigs");
                    if (RF_ModuleEngineConfigs_Type != null)
                    {
                        RF_ModuleEngineConfigs_localCorrectThrust = RF_ModuleEngineConfigs_Type.GetField("localCorrectThrust");
                    }

                    var RF_ModuleHybridEngine_Type = assembly.assembly.GetType("RealFuels.ModuleHybridEngine");
                    if (RF_ModuleHybridEngine_Type != null)
                    {
                        RF_ModuleHybridEngine_localCorrectThrust = RF_ModuleHybridEngine_Type.GetField("localCorrectThrust");
                    }

                    var RF_ModuleHybridEngines_Type = assembly.assembly.GetType("RealFuels.ModuleHybridEngines");
                    if (RF_ModuleHybridEngines_Type != null)
                    {
                        RF_ModuleHybridEngines_localCorrectThrust = RF_ModuleHybridEngines_Type.GetField("localCorrectThrust");
                    }

                    hasInstalledRealFuels = true;
                    break;
                }
                else if (name == "KerbalIspDifficultyScaler")
                {
                    log.AppendLine("Found KIDS mod");

                    var KIDS_Utils_Type = assembly.assembly.GetType("KerbalIspDifficultyScaler.KerbalIspDifficultyScalerUtils");
                    if (KIDS_Utils_Type != null)
                    {
                        KIDS_Utils_GetIspMultiplier = KIDS_Utils_Type.GetMethod("GetIspMultiplier");
                    }

                    KIDSparameters = new object[6];
                    hasInstalledKIDS = true;
                }
            }

            useRFResiduals = hasInstalledRealFuels;
            log.Flush();
        }

        public static bool DoesEngineUseCorrectedThrust(Part theEngine)
        {
            if (hasInstalledRealFuels)
            {
                // Look for any of the Real Fuels engine modules and call the relevant method to find out
                if (RF_ModuleEngineConfigs_localCorrectThrust != null && theEngine.Modules.Contains("ModuleEngineConfigs"))
                {
                    var modEngineConfigs = theEngine.Modules["ModuleEngineConfigs"];
                    if (modEngineConfigs != null)
                    {
                        // Return the localCorrectThrust
                        return (bool)RF_ModuleEngineConfigs_localCorrectThrust.GetValue(modEngineConfigs);
                    }
                }

                if (RF_ModuleHybridEngine_localCorrectThrust != null && theEngine.Modules.Contains("ModuleHybridEngine"))
                {
                    var modHybridEngine = theEngine.Modules["ModuleHybridEngine"];
                    if (modHybridEngine != null)
                    {
                        // Return the localCorrectThrust
                        return (bool)RF_ModuleHybridEngine_localCorrectThrust.GetValue(modHybridEngine);
                    }
                }

                if (RF_ModuleHybridEngines_localCorrectThrust != null && theEngine.Modules.Contains("ModuleHybridEngines"))
                {
                    var modHybridEngines = theEngine.Modules["ModuleHybridEngines"];
                    if (modHybridEngines != null)
                    {
                        // Return the localCorrectThrust
                        return (bool)RF_ModuleHybridEngines_localCorrectThrust.GetValue(modHybridEngines);
                    }
                }
            }

            if (hasInstalledKIDS && HighLogic.LoadedSceneIsEditor)
            {
                return bKIDSThrustISP;
            }

            return false;
        }

        public static void UpdateModSettings()
        {
            if (!hasCheckedForMods)
            {
                CheckForMods();
            }

            if (hasInstalledKIDS)
            {
                // (out ispMultiplierVac, out ispMultiplierAtm, out extendToZeroIsp, out thrustCorrection, out ispCutoff, out thrustCutoff);
                KIDSparameters.Initialize();
                KIDS_Utils_GetIspMultiplier.Invoke(null, KIDSparameters);
                bKIDSThrustISP = (bool)KIDSparameters[3];
            }
        }

        public static String GetVesselTypeString(VesselType vesselType)
        {
            switch (vesselType)
            {
                case VesselType.Debris:
                    return "Debris";
                case VesselType.SpaceObject:
                    return "SpaceObject";
                case VesselType.Unknown:
                    return "Unknown";
                case VesselType.Probe:
                    return "Probe";
                case VesselType.Rover:
                    return "Rover";
                case VesselType.Lander:
                    return "Lander";
                case VesselType.Ship:
                    return "Ship";
                case VesselType.Station:
                    return "Station";
                case VesselType.Base:
                    return "Base";
                case VesselType.EVA:
                    return "EVA";
                case VesselType.Flag:
                    return "Flag";
            }
            return "Undefined";
        }

    }
}
