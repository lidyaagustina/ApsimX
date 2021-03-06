using APSIM.Shared.Utilities;
using Models.Core;
using Models.Interfaces;
using Models.PMF.Functions;
using Models.PMF.Interfaces;
using Models.PMF.Phen;
using System;

namespace Models.PMF.Organs
{
    /// <summary>
    /// This plant organ is parameterised using a simple leaf organ type which provides the core functions of intercepting radiation, providing a photosynthesis supply and a transpiration demand.  It also calculates the growth, senescence and detachment of leaves.
    /// </summary>
    [Serializable]
    [ViewName("UserInterface.Views.GridView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    public class PerennialLeaf : GenericOrgan, ICanopy, ILeaf
    {
        /// <summary>The met data</summary>
        [Link]
        public IWeather MetData = null;


        #region Leaf Interface
        /// <summary>
        /// 
        /// </summary>
        public bool CohortsInitialised { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int TipsAtEmergence { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int CohortsAtInitialisation { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public double InitialisedCohortNo { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public double AppearedCohortNo { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public double PlantAppearedLeafNo { get; set; }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="proprtionRemoved"></param>
        public void DoThin(double proprtionRemoved) { }
        #endregion

        #region Canopy interface

        /// <summary>Gets the canopy. Should return null if no canopy present.</summary>
        public string CanopyType { get { return Plant.CropType; } }

        /// <summary>Albedo.</summary>
        [Description("Albedo")]
        public double Albedo { get; set; }

        /// <summary>Gets or sets the gsmax.</summary>
        [Description("GSMAX")]
        public double Gsmax { get; set; }

        /// <summary>Gets or sets the R50.</summary>
        [Description("R50")]
        public double R50 { get; set; }

        /// <summary>Gets the LAI</summary>
        [Units("m^2/m^2")]
        public double LAI { get; set; }

        /// <summary>Gets the LAI live + dead (m^2/m^2)</summary>
        public double LAITotal { get { return LAI + LAIDead; } }

        /// <summary>Gets the cover green.</summary>
        [Units("0-1")]
        public double CoverGreen
        {
            get
            {
                if (Plant.IsAlive)
                {
                    double greenCover = 1.0 - Math.Exp(-ExtinctionCoefficient.Value * LAI);
                    return Math.Min(Math.Max(greenCover, 0.0), 0.999999999); // limiting to within 10^-9, so MicroClimate doesn't complain
                }
                else
                    return 0.0;
            }
        }

        /// <summary>Gets the cover total.</summary>
        [Units("0-1")]
        public double CoverTotal
        {
            get { return 1.0 - (1 - CoverGreen) * (1 - CoverDead); }
        }

        /// <summary>Gets or sets the height.</summary>
        [Units("mm")]
        public double Height { get; set; }
        /// <summary>Gets the depth.</summary>
        [Units("mm")]
        public double Depth { get { return Height; } }//  Fixme.  This needs to be replaced with something that give sensible numbers for tree crops

        /// <summary>Gets or sets the FRGR.</summary>
        [Units("mm")]
        public double FRGR { get; set; }

        /// <summary>Sets the potential evapotranspiration. Set by MICROCLIMATE.</summary>
        public double PotentialEP { get; set; }

        /// <summary>Sets the light profile. Set by MICROCLIMATE.</summary>
        public CanopyEnergyBalanceInterceptionlayerType[] LightProfile { get; set; }
        #endregion

        #region Parameters
        /// <summary>The FRGR function</summary>
        [Link]
        IFunction FRGRFunction = null;   // VPD effect on Growth Interpolation Set
        /// <summary>The dm demand function</summary>
        [Link]
        IFunction DMDemandFunction = null;

        /// <summary>The lai function</summary>
        [Link]
        IFunction LAIFunction = null;
        /// <summary>The extinction coefficient function</summary>
        [Link]
        IFunction ExtinctionCoefficient = null;
        /// <summary>The photosynthesis</summary>
        [Link]
        IFunction Photosynthesis = null;
        /// <summary>The height function</summary>
        [Link]
        IFunction HeightFunction = null;
        /// <summary>The lai dead function</summary>
        [Link]
        IFunction LaiDeadFunction = null;
        /// <summary>The structural fraction</summary>
        [Link]
        IFunction StructuralFraction = null;

        /// <summary>The structure</summary>
        [Link(IsOptional = true)]
        public Structure Structure = null;
        /// <summary>The phenology</summary>
        [Link(IsOptional = true)]
        public Phenology Phenology = null;
        /// <summary>Water Demand Function</summary>
        [Link(IsOptional = true)]
        IFunction WaterDemandFunction = null;


        #endregion

        #region States and variables

        /// <summary>Gets or sets the k dead.</summary>
        public double KDead { get; set; }                  // Extinction Coefficient (Dead)
        /// <summary>Gets or sets the water demand.</summary>
        [Units("mm")]
        public override double WaterDemand
        {
            get
            {
                if (WaterDemandFunction != null)
                    return WaterDemandFunction.Value;
                else
                    return PotentialEP;
            }
        }
        /// <summary>Gets the transpiration.</summary>
        public double Transpiration { get { return WaterAllocation; } }

        /// <summary>Gets the fw.</summary>
        public double Fw { get { return MathUtilities.Divide(WaterAllocation, WaterDemand, 1); } }

        /// <summary>Gets the function.</summary>
        public double Fn { get { return MathUtilities.Divide(Live.N, Live.Wt * MaximumNConc.Value, 1); } }

        /// <summary>Gets or sets the lai dead.</summary>
        public double LAIDead { get; set; }


        /// <summary>Gets the cover dead.</summary>
        public double CoverDead { get { return 1.0 - Math.Exp(-KDead * LAIDead); } }

        /// <summary>Gets the RAD int tot.</summary>
        [Units("MJ/m^2/day")]
        [Description("This is the intercepted radiation value that is passed to the RUE class to calculate DM supply")]
        public double RadIntTot { get { return CoverGreen * MetData.Radn; } }

        #endregion

        #region Arbitrator Methods
        /// <summary>Gets or sets the water allocation.</summary>
        public override double WaterAllocation { get; set; }
        
        /// <summary>Gets or sets the dm demand.</summary>
        public override BiomassPoolType DMDemand
        {
            get
            {
                StructuralDMDemand = DMDemandFunction.Value;
                NonStructuralDMDemand = 0;
                return new BiomassPoolType { Structural = StructuralDMDemand , NonStructural = NonStructuralDMDemand };
            }
        }

        /// <summary>Gets or sets the dm supply.</summary>
        public override BiomassSupplyType DMSupply
        {
            get
            {
                return new BiomassSupplyType
                {
                    Fixation = Photosynthesis.Value,
                    Retranslocation = AvailableDMRetranslocation(),
                    Reallocation = 0.0
                };
            }
        }

        /// <summary>Gets or sets the n demand.</summary>
        public override BiomassPoolType NDemand
        {
            get
            {
                double StructuralDemand = MaximumNConc.Value * PotentialDMAllocation * StructuralFraction.Value;
                double NDeficit = Math.Max(0.0, MaximumNConc.Value * (Live.Wt + PotentialDMAllocation) - Live.N) - StructuralDemand;
                return new BiomassPoolType { Structural = StructuralDemand, NonStructural = NDeficit };
            }
        }

        #endregion

        #region Events


        /// <summary>Called when [do daily initialisation].</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("DoDailyInitialisation")]
        protected override void OnDoDailyInitialisation(object sender, EventArgs e)
        {
            if (Phenology != null)
                if (Phenology.OnDayOf("Emergence"))
                    if (Structure != null)
                        Structure.LeafTipsAppeared = 1.0;
        }
        #endregion

        #region Component Process Functions

        /// <summary>Clears this instance.</summary>
        protected override void Clear()
        {
            base.Clear();
            Height = 0;
            LAI = 0;
        }
        #endregion

        #region Top Level time step functions
        /// <summary>Event from sequencer telling us to do our potential growth.</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("DoPotentialPlantGrowth")]
        private new void OnDoPotentialPlantGrowth(object sender, EventArgs e)
        {
            base.OnDoPotentialPlantGrowth(sender, e);
            if (Plant.IsEmerged)
            {
                FRGR = FRGRFunction.Value;
                LAI = LAIFunction.Value;
                Height = HeightFunction.Value;
                LAIDead = LaiDeadFunction.Value;
            }
        }

        #endregion

    }
}
