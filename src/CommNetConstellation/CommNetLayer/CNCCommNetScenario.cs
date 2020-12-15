﻿using CommNet;
using CommNetConstellation.UI;
using KSP.UI.Screens.Flight;
using System;
using System.Collections.Generic;
using System.Reflection;
using CommNetManagerAPI;

namespace CommNetConstellation.CommNetLayer
{
    /// <summary>
    /// This class is the key that allows to break into and customise KSP's CommNet. This is possibly the secondary model in the Model–view–controller sense
    /// </summary>
    [KSPScenario(ScenarioCreationOptions.AddToAllGames, new GameScenes[] {GameScenes.FLIGHT, GameScenes.TRACKSTATION, GameScenes.EDITOR, GameScenes.SPACECENTER })]
    public class CNCCommNetScenario : CommNetScenario
    {
        /* Note:
         * 1) On entering a desired scene, OnLoad() and then Start() are called.
         * 2) On leaving the scene, OnSave() is called
         * 3) GameScenes.SPACECENTER is recommended so that the constellation data can be verified and error-corrected in advance
         */

        private CNCCommNetUI CustomCommNetUI = null;
        private CommNetNetwork CustomCommNetNetwork = null;
        private CNCTelemetryUpdate CustomCommNetTelemetry = null;
        private CNCCommNetUIModeButton CustomCommNetModeButton = null;
        public List<Constellation> constellations = new List<Constellation>();
        public List<CNCCommNetHome> groundStations = new List<CNCCommNetHome>();
        private List<CNCCommNetHome> persistentGroundStations = new List<CNCCommNetHome>();
        private List<CNCCommNetVessel> commVessels;
        private bool dirtyCommNetVesselList;
        public bool hideGroundStations;

        public static new CNCCommNetScenario Instance
        {
            get;
            protected set;
        }

        protected override void Start()
        {
            if (!CommNetManagerChecker.CommNetManagerInstalled)
            {
                CNCLog.Error("CommNet Manager is not installed! Please download and install this to use CommNet Constellation.");
                return;
            }

            CNCCommNetScenario.Instance = this;
            this.commVessels = new List<CNCCommNetVessel>();
            this.dirtyCommNetVesselList = true;

            CNCLog.Verbose("CommNet Scenario loading ...");

            //Issue #13: Commnet behaves like vanilla when joining a DMP server for the second time.
            //if stock CommNet logic somehow runs (such as the order of CNCCommNetScenario and CommNetScenario in persisten.sfs)
            if (CommNetScenario.Instance != null)
            {
                UnityEngine.Object.DestroyImmediate(CommNetScenario.Instance);
                CNCCommNetScenario.Instance = this;
            }

            //Replace the CommNet user interface
            CommNetUI ui = FindObjectOfType<CommNetUI>(); // the order of the three lines is important
            CustomCommNetUI = gameObject.AddComponent<CNCCommNetUI>(); // gameObject.AddComponent<>() is "new" keyword for Monohebaviour class
            UnityEngine.Object.Destroy(ui);

            //Replace the CommNet network
            CommNetManagerChecker.SetCommNetManagerIfAvailable(this, typeof(CNCCommNetNetwork), out CustomCommNetNetwork);

            //Replace the TelemetryUpdate
            TelemetryUpdate tel = TelemetryUpdate.Instance; //only appear in flight
            CommNetUIModeButton cnmodeUI = FindObjectOfType<CommNetUIModeButton>(); //only appear in tracking station; initialised separately by TelemetryUpdate in flight
            if (tel != null && HighLogic.LoadedSceneIsFlight)
            {
                TelemetryUpdateData tempData = new TelemetryUpdateData(tel);
                UnityEngine.Object.DestroyImmediate(tel); //seem like UE won't initialise CNCTelemetryUpdate instance in presence of TelemetryUpdate instance
                CustomCommNetTelemetry = gameObject.AddComponent<CNCTelemetryUpdate>();
                CustomCommNetTelemetry.copyOf(tempData);
            }
            else if(cnmodeUI != null && HighLogic.LoadedScene == GameScenes.TRACKSTATION)
            {
                CustomCommNetModeButton = cnmodeUI.gameObject.AddComponent<CNCCommNetUIModeButton>();
                CustomCommNetModeButton.copyOf(cnmodeUI);
                UnityEngine.Object.DestroyImmediate(cnmodeUI);
            }

            //Request for CommNetManager instance
            ICommNetManager CNM = (ICommNetManager)CommNetManagerChecker.GetCommNetManagerInstance();
            CNM.SetCommNetTypes();

            //Replace the CommNet ground stations
            groundStations.Clear();
            for(int i=0; i< CNM.Homes.Count; i++)
            {
                var customHome = CNM.Homes[i].Components.Find(x => x is CNCCommNetHome) as CNCCommNetHome;
                groundStations.Add(customHome);
            }
            groundStations.Sort();
            
            //Apply the ground-station changes from persistent.sfs
            for (int i = 0; i < persistentGroundStations.Count; i++)
            {
                if (!groundStations.Exists(x => x.ID.Equals(persistentGroundStations[i].ID))) //need to create ground stations
                {
                    var stockStation = groundStations.Find(x => x.ID.Equals("Kerbin: Baikerbanur")); //not recommended to use KSC as it has additional properties like SurfaceObject (camera)
                    //var additionalHome = ksc.gameObject.AddComponent<RemoteTechCommNetHome>(); //fail
                    //var additionalHome = ksc.gameObject.AddComponent(typeof(RemoteTechCommNetHome)) as RemoteTechCommNetHome; //fail
                    //var additionalHome = gameObject.AddComponent<RemoteTechCommNetHome>(); //working but transform reference reused
                    var additionalStation = UnityEngine.Object.Instantiate<CNCCommNetHome>(stockStation); //working
                    additionalStation.ID = additionalStation.CommNetHome.nodeName = additionalStation.CommNetHome.displaynodeName = persistentGroundStations[i].ID;
                    additionalStation.CommNetHome.isKSC = false;
                    groundStations.Add(additionalStation);

                    CNCLog.Verbose("Custom CommNet Home '{0}' added", persistentGroundStations[i].ID);
                }

                groundStations.Find(x => x.ID.Equals(persistentGroundStations[i].ID)).applySavedChanges(persistentGroundStations[i]);
            }
            persistentGroundStations.Clear();//dont need anymore

            //Imitate stock CommNetScenario.Instance in order to run certain stock functionalities
            //Comment: Vessel.GetControlLevel() has the check on CommNetScenario.Instance != null before calling vessel.connection.GetControlLevel()
            PropertyInfo property = typeof(CommNetScenario).GetProperty("Instance");
            property.DeclaringType.GetProperty("Instance");
            property.SetValue(CommNetScenario.Instance, this, BindingFlags.NonPublic | BindingFlags.Instance, null, null, null);

            //Imitate stock CommNetNetwork.Instance
            if (CustomCommNetNetwork != null)
            {
                PropertyInfo property2 = typeof(CommNetNetwork).GetProperty("Instance");
                property2.DeclaringType.GetProperty("Instance");
                property2.SetValue(CommNetNetwork.Instance, CustomCommNetNetwork, BindingFlags.NonPublic | BindingFlags.Instance, null, null, null);
            }

            CNCLog.Verbose("CommNet Scenario loading done!");
        }

        public override void OnAwake()
        {
            //override to turn off CommNetScenario's instance check

            CNCCommNetScenario.Instance = this;

            GameEvents.onVesselCreate.Add(new EventData<Vessel>.OnEvent(this.onVesselCountChanged));
            GameEvents.onVesselDestroy.Add(new EventData<Vessel>.OnEvent(this.onVesselCountChanged));
        }

        private void OnDestroy()
        {
            if (CNCCommNetScenario.Instance != null)
            {
                if (this.CustomCommNetUI != null)
                {
                    this.CustomCommNetUI.Destroy(); //CommNetScenario is destroyed first before CommNetUI is destroyed so need to manually trigger CommNetUI's custom destroy logic first
                    UnityEngine.Object.Destroy(this.CustomCommNetUI);
                }

                if (this.CustomCommNetNetwork != null)
                    UnityEngine.Object.Destroy(this.CustomCommNetNetwork);

                if (this.CustomCommNetTelemetry != null)
                    UnityEngine.Object.Destroy(this.CustomCommNetTelemetry);

                if (this.CustomCommNetModeButton != null)
                    UnityEngine.Object.Destroy(this.CustomCommNetModeButton);

                this.commVessels.Clear();

                GameEvents.onVesselCreate.Remove(new EventData<Vessel>.OnEvent(this.onVesselCountChanged));
                GameEvents.onVesselDestroy.Remove(new EventData<Vessel>.OnEvent(this.onVesselCountChanged));

                CNCCommNetScenario.Instance = null;
            }
        }

        public override void OnLoad(ConfigNode gameNode)
        {
            try
            {
                CNCLog.Verbose("Scenario content to be read:\n{0}", gameNode);

                //Other variables
                for (int i = 0; i < gameNode.values.Count; i++)
                {
                    ConfigNode.Value value = gameNode.values[i];
                    string name = value.name;
                    switch (name)
                    {
                        case "DisplayModeTracking":
                            CNCCommNetUI.CustomModeTrackingStation = (CNCCommNetUI.CustomDisplayMode)((int)Enum.Parse(typeof(CNCCommNetUI.CustomDisplayMode), value.value));
                            break;
                        case "DisplayModeFlight":
                            CNCCommNetUI.CustomModeFlightMap = (CNCCommNetUI.CustomDisplayMode)((int)Enum.Parse(typeof(CNCCommNetUI.CustomDisplayMode), value.value));
                            break;
                        case "HideGroundStations":
                            this.hideGroundStations = Boolean.Parse(value.value);
                            break;
                        case "LegacyOrbitLineColor":
                            CNCSettings.Instance.LegacyOrbitLineColor = Boolean.Parse(value.value);
                            break;
                    }
                }

                //Constellations
                if (gameNode.HasNode("Constellations"))
                {
                    ConfigNode constellationNode = gameNode.GetNode("Constellations");
                    ConfigNode[] constellationNodes = constellationNode.GetNodes();

                    if (constellationNodes.Length < 1) // missing constellation list
                    {
                        CNCLog.Error("The 'Constellations' node is malformed! Reverted to the default constellation list.");
                        constellations = CNCSettings.Instance.Constellations;
                    }
                    else
                    {
                        constellations.Clear();
                        for (int i = 0; i < constellationNodes.Length; i++)
                        {
                            Constellation newConstellation = new Constellation();
                            ConfigNode.LoadObjectFromConfig(newConstellation, constellationNodes[i]);
                            constellations.Add(newConstellation);
                        }
                    }
                }
                else
                {
                    CNCLog.Verbose("The 'Constellations' node is not found. The default constellation list is loaded.");
                    constellations = CNCSettings.Instance.Constellations;
                }

                constellations.Sort();

                //Ground stations
                if (gameNode.HasNode("GroundStations"))
                {
                    ConfigNode stationNode = gameNode.GetNode("GroundStations");
                    ConfigNode[] stationNodes = stationNode.GetNodes();

                    if (stationNodes.Length < 1) // missing ground-station list
                    {
                        CNCLog.Error("The 'GroundStations' node is malformed! Reverted to the default list of ground stations.");
                        persistentGroundStations = CNCSettings.Instance.GroundStations;
                    }
                    else
                    {
                        persistentGroundStations.Clear();
                        for (int i = 0; i < stationNodes.Length; i++)
                        {
                            CNCCommNetHome dummyGroundStation = new CNCCommNetHome();
                            ConfigNode.LoadObjectFromConfig(dummyGroundStation, stationNodes[i]);
                            if(!stationNodes[i].HasNode("Frequencies")) // empty list is not saved as empty node in persistent.sfs
                            {
                                dummyGroundStation.deleteFrequencies();// clear the default frequency list
                            }
                            persistentGroundStations.Add(dummyGroundStation);
                        }
                    }
                }
                else
                {
                    CNCLog.Verbose("The 'GroundStations' node is not found. The default list of ground stations is loaded from KSP's data.");
                    persistentGroundStations = CNCSettings.Instance.GroundStations;
                }
            }
            catch (Exception e)
            {
                CNCLog.Error("Error when loading CNCCommNetScenario: {0}", e.Message);
            }
        }

        public override void OnSave(ConfigNode gameNode)
        {
            try
            {
                //Other variables
                gameNode.AddValue("DisplayModeTracking", CNCCommNetUI.CustomModeTrackingStation);
                gameNode.AddValue("DisplayModeFlight", CNCCommNetUI.CustomModeFlightMap);
                gameNode.AddValue("HideGroundStations", this.hideGroundStations);
                gameNode.AddValue("LegacyOrbitLineColor", CNCSettings.Instance.LegacyOrbitLineColor);

                //Constellations
                if (gameNode.HasNode("Constellations"))
                {
                    gameNode.RemoveNode("Constellations");
                }

                ConfigNode constellationNode = new ConfigNode("Constellations");
                for (int i = 0; i < constellations.Count; i++)
                {
                    ConfigNode newConstellationNode = new ConfigNode("Constellation");
                    newConstellationNode = ConfigNode.CreateConfigFromObject(constellations[i], newConstellationNode);
                    constellationNode.AddNode(newConstellationNode);
                }

                if (constellations.Count <= 0)
                {
                    CNCLog.Error("No user-defined constellations to save!");
                }
                else
                {
                    gameNode.AddNode(constellationNode);
                }

                //Ground stations
                if (gameNode.HasNode("GroundStations"))
                {
                    gameNode.RemoveNode("GroundStations");
                }

                ConfigNode stationNode = new ConfigNode("GroundStations");
                for (int i = 0; i < groundStations.Count; i++)
                {
                    ConfigNode newGroundStationNode = new ConfigNode("GroundStation");
                    newGroundStationNode = ConfigNode.CreateConfigFromObject(groundStations[i], newGroundStationNode);
                    stationNode.AddNode(newGroundStationNode);
                }

                if (groundStations.Count <= 0)
                {
                    CNCLog.Error("No ground stations to save!");
                }
                else
                {
                    gameNode.AddNode(stationNode);
                }

                CNCLog.Verbose("Scenario content to be saved:\n{0}", gameNode);
            }
            catch (Exception e)
            {
                CNCLog.Error("Error when saving CNCCommNetScenario: {0}", e.Message);
            }
        }

        /// <summary>
        /// Obtain all communicable vessels that have the given frequency
        /// </summary>
        public List<CNCCommNetVessel> getCommNetVessels(short targetFrequency = -1)
        {
            cacheCommNetVessels();

            List<CNCCommNetVessel> newList = new List<CNCCommNetVessel>();
            for(int i=0; i<commVessels.Count; i++)
            {
                if (targetFrequency == -1 || GameUtils.firstCommonElement(commVessels[i].getFrequencyArray(), new short[] { targetFrequency }) >= 0)
                    newList.Add(commVessels[i]);
            }

            return newList;
        }

        /// <summary>
        /// Find the vessel that has the given comm node
        /// </summary>
        public Vessel findCorrespondingVessel(CommNode commNode)
        {
            cacheCommNetVessels();

            return commVessels.Find(x => CNCCommNetwork.AreSame(x.CommNetVessel.Comm, commNode)).Vessel; // more specific equal
            //IEqualityComparer<CommNode> comparer = commNode.Comparer; // a combination of third-party mods somehow  affects CommNode's IEqualityComparer on two objects
            //return commVessels.Find(x => comparer.Equals(commNode, x.Comm)).Vessel;
        }

        /// <summary>
        /// Find the ground station that has the given comm node
        /// </summary>
        public CNCCommNetHome findCorrespondingGroundStation(CommNode commNode)
        {
            return groundStations.Find(x => CNCCommNetwork.AreSame(x.comm, commNode));
        }

        /// <summary>
        /// Cache eligible vessels of the FlightGlobals
        /// </summary>
        private void cacheCommNetVessels()
        {
            if (!this.dirtyCommNetVesselList)
                return;

            CNCLog.Verbose("CommNetVessel cache - {0} entries deleted", this.commVessels.Count);
            this.commVessels.Clear();

            try
            {
                List<Vessel> allVessels = FlightGlobals.fetch.vessels;
                for (int i = 0; i < allVessels.Count; i++)
                {
                    if (allVessels[i].connection != null && 
                        ((allVessels[i].connection as IModularCommNetVessel).GetModuleOfType<CNCCommNetVessel>().IsCommandable && allVessels[i].vesselType != VesselType.Unknown)// && allVessels[i].vesselType != VesselType.Debris) // debris could be spent stage with functional probes and antennas
                        || (allVessels[i].vesselType == VesselType.DeployedScienceController))
                    {
                        CNCLog.Debug("Caching CommNetVessel '{0}'", allVessels[i].vesselName);
                        this.commVessels.Add(((IModularCommNetVessel)allVessels[i].connection).GetModuleOfType<CNCCommNetVessel>());
                    }
                }

                CNCLog.Verbose("CommNetVessel cache - {0} entries added", this.commVessels.Count);
            }
            catch(NullReferenceException e)
            {
                //Singleton CommNetVessel class in KSP
                CNCLog.Error("CNCCommNetScenario - Conflict with third-party CommNet mod(s)! Please remove this or other mod(s)");
            }

            this.dirtyCommNetVesselList = false;
        }

        /// <summary>
        /// GameEvent call for newly-created vessels (launch, staging, new asteriod etc)
        /// NOTE: Vessel v is fresh bread straight from the oven before any curation is done on this (i.e. debris.Connection is valid)
        /// </summary>
        private void onVesselCountChanged(Vessel v)
        {
            if (v.vesselType == VesselType.Base || v.vesselType == VesselType.Lander || v.vesselType == VesselType.Plane ||
               v.vesselType == VesselType.Probe || v.vesselType == VesselType.Relay || v.vesselType == VesselType.Rover ||
               v.vesselType == VesselType.Ship || v.vesselType == VesselType.Station || v.vesselType == VesselType.DeployedScienceController)
            {
                CNCLog.Debug("Change in the vessel list detected. Cache refresh required.");
                this.dirtyCommNetVesselList = true;
            }
        }

        /// <summary>
        /// Convenient method to obtain a frequency list from a given CommNode
        /// </summary>
        public short[] getFrequencies(CommNode a)
        {
            if (a.isHome && findCorrespondingGroundStation(a) != null)
            {
                return findCorrespondingGroundStation(a).getFrequencyArray();
            }
            else
            {
                var cncvessel = ((IModularCommNetVessel)a.GetVessel().Connection).GetModuleOfType<CNCCommNetVessel>();
                return cncvessel.getFrequencyArray();
            }
        }

        /// <summary>
        /// Convenient method to obtain Comm Power of given frequency from a given CommNode
        /// </summary>
        public double getCommPower(CommNode a, short frequency)
        {
            double power = 0.0;

            if (a.isHome && findCorrespondingGroundStation(a) != null)
            {
                power = a.antennaRelay.power;
            }
            else
            {
                var cncvessel = ((IModularCommNetVessel)a.GetVessel().Connection).GetModuleOfType<CNCCommNetVessel>();
                power = cncvessel.getMaxComPower(frequency);
            }

            return power;
        }
    }
}
