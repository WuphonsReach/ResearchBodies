﻿/*
 * ResearchBodiescontroller.cs
 * (C) Copyright 2016, Jamie Leighton  
 * License : MIT 
 * Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
 * The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
 *
 * Original code was developed by 
 * Kerbal Space Program is Copyright (C) 2013 Squad. See http://kerbalspaceprogram.com/. This
 * project is in no way associated with nor endorsed by Squad.
 *
 */
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Contracts;
using KSP.UI.Screens.Mapview;
using RSTUtils;

namespace ResearchBodies
{
    public partial class ResearchBodiesController : MonoBehaviour
    {
        internal bool isTSTInstalled = false;
        internal bool isPCBMInstalled = false;
        
        public static ResearchBodiesController instance;

        private ResearchBodiesInstructor instructor_Werner;
        private ResearchBodiesInstructor instructor_Linus;
        
        public void Awake()
        {
            instance = this;
            _RBwindowId = Utilities.getnextrandomInt();
        }

        public void Start()
        {
            French = HighLogic.CurrentGame.Parameters.CustomParams<ResearchBodies_SettingsParms>().french;
            isTSTInstalled = Database.instance.isTSTInstalled;
            isPCBMInstalled = Utilities.IsPCBMInstalled;
            if (isPCBMInstalled)  //If Progressive CB Maps assembly is present, initialise PCBM wrapper.
            {
                PCBMWrapper.InitPCBMWrapper();
                if (!PCBMWrapper.APIPCBMReady)
                {
                    isPCBMInstalled = false; //If the initialise of wrapper failed set bool to false, we won't be interfacing to PCBM today.
                }
            }
            enable = ResearchBodies.Enabled;
            
            //Create Instructor
            instructor_Werner = new ResearchBodiesInstructor("Instructor_Wernher");
            instructor_Linus = new ResearchBodiesInstructor("Strategy_ScienceGuy");
            instructor_Werner.Instructor.enabled = false;
            instructor_Linus.Instructor.enabled = false;
            //Register for Contract On offerred so we can remove ones that are for bodies not yet tracked.
            if (HighLogic.CurrentGame.Mode == Game.Modes.CAREER)
                GameEvents.Contract.onOffered.Add(CheckContracts);

            if (Utilities.IsKopInstalled)
            {
                Database.instance.ReApplyRanges();
            }

            //If RB is enabled set initial Discovery Levels of CBs and call ProgressiveCBMaps to set their graphics levels.
            if (enable)
            {
                SetBodyDiscoveryLevels();
                GameEvents.onVesselSOIChanged.Add(onVesselSOIChanged);
                Utilities.setScaledScreen();
                windowRect = new Rect(1, 1, Utilities.scaledScreenWidth-2, Utilities.scaledScreenHeight-2);
                GameEvents.onScreenResolutionModified.Add(onScreenResolutionModified);
                GameEvents.OnMapEntered.Add(onMapEntered);
                if (HighLogic.LoadedScene == GameScenes.TRACKSTATION)
                {
                    onMapEntered();
                }
            }
            else
            {
                Database.instance.ResetBodyVisibilities();
                SetBodyDiscoveryLevels();
            }
        }

        public void OnDestroy()
        {
            instructor_Werner.Destroy();
            if (instructor_Werner.Instructor != null)
                Destroy(instructor_Werner.Instructor.gameObject);
            instructor_Linus.Destroy();
            if (instructor_Linus.Instructor != null)
                Destroy(instructor_Linus.Instructor.gameObject);
            GameEvents.onVesselSOIChanged.Remove(onVesselSOIChanged);
            GameEvents.onScreenResolutionModified.Remove(onScreenResolutionModified);
            GameEvents.OnMapEntered.Remove(onMapEntered);
            if (HighLogic.CurrentGame.Mode == Game.Modes.CAREER)
                GameEvents.Contract.onOffered.Remove(CheckContracts);
        }
        
        /// <summary>
        /// Called by GameEvent onOffered. 
        /// If Contract name == "ConfiguredContract" it's a Contract Configurator mod contract, which is RB aware, so ignore it.
        /// Will check contract parameters for reference to untracked bodies.
        /// If it finds a reference it will Withdraw the contract.
        /// </summary>
        /// <param name="contract"></param>
        private void CheckContracts(Contract contract)
        {
            //Ignore Contract Configurator Contracts.
            if (contract.GetType().Name == "ConfiguredContract")
            {
                return;
            }
            
            foreach (ContractParameter cp in contract.AllParameters.ToList())
            {

                foreach (KeyValuePair<CelestialBody, CelestialBodyInfo> body in Database.instance.CelestialBodies) 
                {
                        if (!Database.instance.CelestialBodies[body.Key].isResearched && cp.Title.Contains(body.Key.GetName()))
                        {
                            TryWithDrawContract(contract);
                            break;
                        }
                    
                }
            }
            
        }
        private void TryWithDrawContract(Contract c)
        {
            try
            {
                //RSTLogWriter.Log("WithDrew contract \"{0}\"" , c.Title);
                c.Withdraw(); //Changed to Withdraw - this will not penalize reputation.
            }
            catch (Exception e)
            {
                RSTLogWriter.Log("Unable to Withraw contract ! {0}" , e);
                RSTLogWriter.Flush();
            }
        }

        /// <summary>
        /// When a SOI change is triggered by GameEvents we check if the TO body Is Discovered or NOT.
        /// If it is not discovered we make it discovered.
        /// If it's researchState is less than 100 we set it to 100.
        /// Finally we set the discovery Level and ProgressiveCBMap.
        /// todo: change this dynamic to discover on SOI entry but gradually build up researchState over time as it remains in SOI.
        /// </summary>
        /// <param name="HostedfromtoAction"></param>
        private void onVesselSOIChanged(GameEvents.HostedFromToAction<Vessel, CelestialBody> HostedfromtoAction)
        {
            if (Database.instance.CelestialBodies.ContainsKey(HostedfromtoAction.to))
            {
                if (!Database.instance.CelestialBodies[HostedfromtoAction.to].isResearched)
                {
                    bool withparent;
                    CelestialBody parentCB;
                    FoundBody(0, HostedfromtoAction.to, out withparent, out parentCB);
                }
                if (Database.instance.CelestialBodies[HostedfromtoAction.to].researchState < 100)
                { 
                    Database.instance.CelestialBodies[HostedfromtoAction.to].researchState = 100;
                    //ScreenMessages.PostScreenMessage(string.Format(Locales.currentLocale.Values["#autoLOC_RBodies_00012"],HostedfromtoAction.to.GetName(), Database.Instance.RB_SettingsParms.ScienceReward), 5f);
                    //ResearchAndDevelopment.Instance.AddScience(Database.Instance.RB_SettingsParms.ScienceReward,TransactionReasons.None);
                    ScreenMessages.PostScreenMessage(Locales.FmtLocaleString("#autoLOC_RBodies_00012", HostedfromtoAction.to.displayName, Database.instance.RB_SettingsParms.ScienceReward.ToString()), 5f);
                    ResearchAndDevelopment.Instance.AddScience(Database.instance.RB_SettingsParms.ScienceReward, TransactionReasons.None);
                    var keyvalue = Database.instance.CelestialBodies.FirstOrDefault(a => a.Key.bodyName == HostedfromtoAction.to.bodyName);
                    if (keyvalue.Key != null)
                    {
                        SetIndividualBodyDiscoveryLevel(keyvalue);
                    }
                }
            }
        }

        /// <summary>
        /// Returns True if the Trackstation is Level 1 otherwise False.
        /// </summary>
        public bool IsTSlevel1
        {
            get { return PSystemSetup.Instance.GetSpaceCenterFacility("TrackingStation").GetFacilityLevel() < 0.5; }
        }

        /// <summary>
        /// Call this to signal that you have found a celestial body.
        /// It will set the celestial body to discovered.
        /// </summary>
        /// <param name="scienceReward">The scienceReward additional amount that is added to the base scienceReward for finding a body</param>
        /// <param name="bodyFound">The Celestial Body found</param>
        /// <param name="withParent">Will return true if the Parent Body has also been found</param>
        /// <param name="parentBody">The parent body that was discovered as well if withParent is true</param>
        /// <returns></returns>
        public static bool FoundBody(int scienceReward, CelestialBody bodyFound, out bool withParent, out CelestialBody parentBody)
        {
            withParent = false;
            parentBody = null;
            if (HighLogic.CurrentGame.Mode != Game.Modes.SANDBOX)  //If not sandbox add the Science Points reward!
            {
                //var sciencePtsReward = scienceReward + Database.Instance.RB_SettingsParms.ScienceReward;
                var sciencePtsReward = scienceReward + Database.instance.RB_SettingsParms.ScienceReward;
                ResearchAndDevelopment.Instance.AddScience(sciencePtsReward, TransactionReasons.None);
                ScreenMessages.PostScreenMessage("Added " + sciencePtsReward + " science points !", 5f);
            }
            //Check if the referencebody is also not known. If so, we discover both the body and it's referencebody (parent).
            if (bodyFound.referenceBody != null && bodyFound != bodyFound.referenceBody && bodyFound.referenceBody.DiscoveryInfo.Level == DiscoveryLevels.Presence)
            {
                CelestialBody cbKey = Database.instance.ContainsBodiesKey(bodyFound.referenceBody.bodyName);
                if (cbKey != null)
                    Database.instance.CelestialBodies[cbKey].isResearched = true;
                cbKey = Database.instance.ContainsBodiesKey(bodyFound.bodyName);
                if (cbKey != null)
                {
                    Database.instance.CelestialBodies[cbKey].isResearched = true;
                    var tempEntry = new KeyValuePair<CelestialBody, CelestialBodyInfo>(bodyFound, Database.instance.CelestialBodies[bodyFound]);
                    setCBContractWeight(tempEntry, false);
                }
                withParent = true;
                parentBody = bodyFound.referenceBody;
                //check for Barycenter as well.
                if (bodyFound.referenceBody.referenceBody != null)
                {
                    if ((bodyFound.referenceBody.referenceBody.DiscoveryInfo.Level == DiscoveryLevels.Appearance
                        || bodyFound.referenceBody.referenceBody.DiscoveryInfo.Level == DiscoveryLevels.Presence) &&
                        Database.instance.CelestialBodies.ContainsKey(bodyFound.referenceBody.referenceBody))
                    {
                        Database.instance.CelestialBodies[bodyFound.referenceBody.referenceBody].isResearched = true;
                        var tempEntry = new KeyValuePair<CelestialBody, CelestialBodyInfo>(bodyFound.referenceBody.referenceBody, Database.instance.CelestialBodies[bodyFound.referenceBody.referenceBody]);
                        setCBContractWeight(tempEntry, false);
                    }
                }
                RSTLogWriter.Log("Found body {0} orbiting around {1} !", bodyFound.GetName(), bodyFound.referenceBody.GetName());
            }
            else //No parent or parent is already discovered. So we just found this body.
            {
                CelestialBody cbKey = Database.instance.ContainsBodiesKey(bodyFound.bodyName);
                if (cbKey != null)
                {
                    Database.instance.CelestialBodies[cbKey].isResearched = true;
                    var tempEntry = new KeyValuePair<CelestialBody, CelestialBodyInfo>(cbKey, Database.instance.CelestialBodies[cbKey]);
                    setCBContractWeight(tempEntry, false);
                }
                withParent = false;
                RSTLogWriter.Log("Found body {0} !", bodyFound.GetName());
            }
            RSTLogWriter.Flush();
            return true;
        }
        public static bool Research(CelestialBody body, int researchToAdd)
        {
            CelestialBody cbKey = Database.instance.ContainsBodiesKey(body.bodyName);
            if (cbKey != null && Database.instance.CelestialBodies[cbKey].researchState < 100)
            {
                if (Funding.Instance != null)
                {
                    //if (Funding.Instance.Funds >= ResearchBodies.Instance.RBgameSettings.ProgressResearchCost)

                    if (Funding.Instance.Funds >= Database.instance.RB_SettingsParms.ProgressResearchCost)
                    {
                        Database.instance.CelestialBodies[cbKey].researchState += researchToAdd;
                        if (Database.instance.CelestialBodies[cbKey].researchState > 100)
                            Database.instance.CelestialBodies[cbKey].researchState = 100;
                        //Funding.Instance.AddFunds(-ResearchBodies.Instance.RBgameSettings.ProgressResearchCost, TransactionReasons.None);
                        Funding.Instance.AddFunds(-Database.instance.RB_SettingsParms.ProgressResearchCost, TransactionReasons.Progression);
                    }
                    else
                    {
                        ScreenMessages.PostScreenMessage(Locales.FmtLocaleString("#autoLOC_RBodies_00047"), 3.0f, ScreenMessageStyle.UPPER_CENTER);
                    }
                }
                else
                {
                    if (HighLogic.CurrentGame.Mode != Game.Modes.CAREER)
                    {
                        Database.instance.CelestialBodies[cbKey].researchState += researchToAdd;
                    }
                }
                KeyValuePair<CelestialBody, CelestialBodyInfo> cb =
                                    new KeyValuePair<CelestialBody, CelestialBodyInfo>(cbKey,Database.instance.CelestialBodies[cbKey]);
                ResearchBodiesController.instance.SetIndividualBodyDiscoveryLevel(cb);
                if (Database.instance.CelestialBodies[cbKey].researchState == 100 && ResearchAndDevelopment.Instance != null)
                {
                    ScreenMessages.PostScreenMessage(Locales.FmtLocaleString("#autoLOC_RBodies_00012", cbKey.displayName, Database.instance.RB_SettingsParms.ScienceReward.ToString()), 5f);
                    //ResearchAndDevelopment.Instance.AddScience(Database.Instance.RB_SettingsParms.ScienceReward, TransactionReasons.None);
                    ResearchAndDevelopment.Instance.AddScience(Database.instance.RB_SettingsParms.ScienceReward, TransactionReasons.None);
                }
                return true;
            }
            else
            {
                return false;
            }
        }
        public static void LaunchResearchPlan(CelestialBody cb)
        {
            CelestialBody cbKey = Database.instance.ContainsBodiesKey(cb.bodyName);
            if (cbKey != null && Database.instance.CelestialBodies[cbKey].researchState == 0)
            {
                if (Funding.Instance != null)
                {
                    //if (Funding.Instance.Funds >= Database.Instance.RB_SettingsParms.ResearchCost)
                    if (Funding.Instance.Funds >= Database.instance.RB_SettingsParms.ResearchCost)
                    {
                        //Funding.Instance.AddFunds(-Database.Instance.RB_SettingsParms.ResearchCost, TransactionReasons.None);
                        Funding.Instance.AddFunds(-Database.instance.RB_SettingsParms.ResearchCost, TransactionReasons.Progression);
                        Research(cbKey, 10);
                    }
                    else
                        ScreenMessages.PostScreenMessage(Locales.FmtLocaleString("#autoLOC_RBodies_00014", cbKey.displayName),3.0f, ScreenMessageStyle.UPPER_CENTER);
                }
                else
                {
                    if (HighLogic.CurrentGame.Mode != Game.Modes.CAREER)
                    {
                        Research(cbKey, 10);
                    }
                }
            }
            else
                RSTLogWriter.Log(Locales.FmtLocaleString("#autoLOC_RBodies_00015", cb.displayName));
        }
        public static void StopResearchPlan(CelestialBody cb)
        {
            CelestialBody cbKey = Database.instance.ContainsBodiesKey(cb.bodyName);
            if (cbKey != null && Database.instance.CelestialBodies[cbKey].researchState >= 10)
            {
                if (Funding.Instance != null)
                {
                    Funding.Instance.AddFunds(Database.instance.RB_SettingsParms.ResearchCost, TransactionReasons.Progression);
                }
                Database.instance.CelestialBodies[cbKey].researchState = 0;
                KeyValuePair<CelestialBody, CelestialBodyInfo> cbd =
                                    new KeyValuePair<CelestialBody, CelestialBodyInfo>(cbKey,Database.instance.CelestialBodies[cbKey]);
                ResearchBodiesController.instance.SetIndividualBodyDiscoveryLevel(cbd);
            }
            else
                RSTLogWriter.Log(Locales.FmtLocaleString("#autoLOC_RBodies_00016", cb.displayName));
        }

        public void onMapEntered()
        {
            if (MapNode.AllMapNodes.Count == 0)
            {
                base.StartCoroutine(CallbackUtil.DelayedCallback(10, new Callback(this.processMapNodes)));
            }
            else
            {
                bool containsCBs = false;
                for(int mnI = 0; mnI < MapNode.AllMapNodes.Count; mnI++)
                {
                    if (MapNode.AllMapNodes[mnI].mapObject != null)
                    {
                        if (MapNode.AllMapNodes[mnI].mapObject.celestialBody != null)
                        {
                            containsCBs = true;
                            break;
                        }
                    }
                }
                if (containsCBs)
                {
                    processMapNodes();
                }
                else
                {
                    base.StartCoroutine(CallbackUtil.DelayedCallback(10, new Callback(this.processMapNodes)));
                }
            }
        }

        private void processMapNodes()
        {
            List<MapNode> allNodes = MapNode.AllMapNodes;
            for (int i = 0; i < allNodes.Count; i++)
            {
                MapNode currentNode = allNodes[i];
                if (currentNode != null)
                {
                    if (currentNode.mapObject != null)
                    {
                        if (currentNode.mapObject.celestialBody != null)
                        {
                            if (Database.instance.CelestialBodies.ContainsKey(currentNode.mapObject.celestialBody))
                            {
                                CelestialBodyInfo dbCBvalue =
                                    Database.instance.CelestialBodies[currentNode.mapObject.celestialBody];
                                if (!dbCBvalue.isResearched)
                                {
                                    currentNode.VisualIconData.iconEnabled = false;
                                }
                                else
                                {
                                    currentNode.VisualIconData.iconEnabled = true;
                                }
                            }
                        }
                    }
                }
            }            
        }

        /// <summary>
        /// Set Discovery Levels of the Bodies
        /// None = 0, Presence = 1 (Object has been detected in tracking station), Name = 4 (Object has been tracked), StateVectors = 8 (Object is currently tracked),
        /// Appearance = 16 (Unlocks mass and type fields; intended for discoverable CelestialBodies?)
        /// </summary>
        public void SetBodyDiscoveryLevels()
        {
            foreach (KeyValuePair<CelestialBody, CelestialBodyInfo> cb in Database.instance.CelestialBodies)
            {
                SetIndividualBodyDiscoveryLevel(cb);
            }
        }

        public void SetIndividualBodyDiscoveryLevel(KeyValuePair<CelestialBody, CelestialBodyInfo> cb)
        {
            if (!cb.Value.ignore)
            {
                if (!cb.Value.isResearched)
                {
                    SetBodyDiscoveryLevel(cb, DiscoveryLevels.Presence);
                }
                else if (cb.Value.isResearched && cb.Value.researchState < 50)
                {
                    SetBodyDiscoveryLevel(cb, DiscoveryLevels.Appearance);
                }
                else
                {
                    SetBodyDiscoveryLevel(cb, DiscoveryLevels.Owned);
                }
            }
            else
            {
                SetBodyDiscoveryLevel(cb, DiscoveryLevels.Owned);
            }
        }

        public void SetBodyDiscoveryLevel(KeyValuePair<CelestialBody, CelestialBodyInfo> cb, DiscoveryLevels level)
        {
            cb.Key.DiscoveryInfo.SetLevel(level);
            try
            {
                if (Database.instance.CelestialBodies[cb.Key.referenceBody].KOPbarycenter)
                    cb.Key.referenceBody.DiscoveryInfo.SetLevel(level);
                if (cb.Value.KOPrelbarycenterBody != null)
                    cb.Value.KOPrelbarycenterBody.DiscoveryInfo.SetLevel(level);
            }
            catch (Exception)
            {// throw;
            }

            if (isPCBMInstalled  && (HighLogic.LoadedScene == GameScenes.FLIGHT || HighLogic.LoadedScene == GameScenes.TRACKSTATION))  //If progressive CB maps are installed set the level of the meshmap.
            {
                if (cb.Value.ignore)
                {
                    setCBContractWeight(cb, true);
                    SetBodyProgressiveCBMap(cb.Key, 6);
                    return;
                }

                if (!cb.Value.isResearched)
                {
                    setCBContractWeight(cb, false);
                    SetBodyProgressiveCBMap(cb.Key, 1);
                }
                else
                {
                    if (cb.Value.researchState < 30)
                    {
                        setCBContractWeight(cb, false);
                        SetBodyProgressiveCBMap(cb.Key, 2);
                    }
                    else
                    {
                        if (cb.Value.researchState < 50)
                        {
                            setCBContractWeight(cb, false);
                            SetBodyProgressiveCBMap(cb.Key, 3);
                        }
                        else
                        {
                            if (cb.Value.researchState < 70)
                            {
                                setCBContractWeight(cb, false);
                                SetBodyProgressiveCBMap(cb.Key, 4);
                            }
                            else
                            {
                                if (cb.Value.researchState < 90)
                                {
                                    setCBContractWeight(cb, false);
                                    SetBodyProgressiveCBMap(cb.Key, 5);
                                }
                                else
                                {
                                    setCBContractWeight(cb, false);
                                    SetBodyProgressiveCBMap(cb.Key, 6);
                                }
                            }
                        }
                    }
                }
            }
        }
        
        /// <summary>
        /// Set Body Graphics levels in ProgressiveCBMaps
        /// </summary>
        public void SetBodyProgressiveCBMap(CelestialBody cb, int level)
        {
            if (PCBMWrapper.actualPCBMAPI.CBVisualMapsInfo != null)
            {
                if (PCBMWrapper.actualPCBMAPI.CBVisualMapsInfo.ContainsKey(cb))
                {
                    PCBMWrapper.actualPCBMAPI.CBVisualMapsInfo[cb].setVisualLevel(level);
                }
            }
        }

        public static void setCBContractWeight(KeyValuePair<CelestialBody, CelestialBodyInfo> cb, bool setzero)
        {
            if (Contracts.ContractSystem.Instance != null)
            {
                //Find the Contract Weight.
                int currentvalue = 0;
                if (Contracts.ContractSystem.ContractWeights.TryGetValue(cb.Key.name, out currentvalue))
                {
                    //If setzero is it not already zero? Save current value then set to zero.
                    //If not setzero restore saved value.
                    if (setzero)
                    {
                        if (currentvalue != 0)
                        {
                            cb.Value.ContractsWeight = currentvalue;
                        }
                        Contracts.ContractSystem.WeightAssignment(cb.Key.name, 0, true);
                    }
                    else
                    {
                        if (cb.Value.ContractsWeight != 0)
                        {
                            Contracts.ContractSystem.WeightAssignment(cb.Key.name, cb.Value.ContractsWeight, true);
                        }
                    }
                }
            }
        }
    }
}
