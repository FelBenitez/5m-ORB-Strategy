#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
using System.Windows;  // for MessageBox
#endregion

//This namespace holds Strategies in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Strategies
{
	public class ORB : Strategy
	{
		private double BoxHigh;
		private double BoxLow;
		private double BoxHeight;
		private bool SessionTradeTaken;
		private DateTime SessionOpen;
		private bool IsLondonSession;
		private double PrevDayHigh, PrevDayLow;
		private bool basicFiltersPassed; // breakout + boxSize + EMA
		private bool allFiltersPassed; // basicFiltersPassed + volume + S/R
		private bool bracketLocked; // freeze the bracked when allFiltersPassed
		private bool breakEvenMoved;
		// Store the actual prices of your filled bracket
		private double filledEntryPrice;
		private double filledStopPrice;


		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"Enter the description for your new custom Strategy here.";
				Name										= "ORB";
				Calculate									= Calculate.OnBarClose;
				EntriesPerDirection							= 1;
				EntryHandling								= EntryHandling.AllEntries;
				IsExitOnSessionCloseStrategy				= true;
				ExitOnSessionCloseSeconds					= 30;
				IsFillLimitOnTouch							= false;
				MaximumBarsLookBack							= MaximumBarsLookBack.TwoHundredFiftySix;
				OrderFillResolution							= OrderFillResolution.Standard;
				Slippage									= 0;
				StartBehavior								= StartBehavior.WaitUntilFlat;
				TimeInForce									= TimeInForce.Gtc;
				TraceOrders									= false;
				RealtimeErrorHandling						= RealtimeErrorHandling.StopCancelClose;
				StopTargetHandling							= StopTargetHandling.PerEntryExecution;
				BarsRequiredToTrade							= 20;
				// Disable this property for performance gains in Strategy Analyzer optimizations
				// See the Help Guide for additional information
				IsInstantiatedOnEachOptimizationIteration	= true;
				Session1Hour					= 2;
				Session1Min					= 0;
				Session2Hour					= 8;
				Session2Min					= 30;
				MinBoxLondon					= 0.75;
				MinBoxNewYork					= 1;
				MaxBox					= 8;
				VolMultiplierLon					= 1.15;
				VolMultiplierNY					= 1.3;
				BoxHigh					= 0;
				BoxLow					= 0;
				BoxHeight					= 0;
				SessionTradeTaken					= false;
				SessionOpen						= DateTime.Parse("00:00", System.Globalization.CultureInfo.InvariantCulture);
				IsLondonSession					= false;
				basicFiltersPassed = allFiltersPassed = bracketLocked = false;
				breakEvenMoved = false;
			}
			else if (State == State.Configure)
			{
				AddDataSeries(BarsPeriodType.Day, 1); // "second" series
				// Tells strategy to subscribe to 2nd series of data: 1-minute bars. 
				AddDataSeries(BarsPeriodType.Minute, 1); // "Third series"
			}
		}

		protected override void OnBarUpdate()
		{
			if(BarsInProgress == 0 && !SessionTradeTaken && PositionAccount.MarketPosition != MarketPosition.Flat)
			{
				SessionTradeTaken = true;
				bracketLocked     = true;          // freeze Entry/SL/TP lines
        		breakEvenMoved    = false;
				
				 filledEntryPrice  = PositionAccount.AveragePrice;
        		// filledStopPrice was captured when we drew the bracket
        		Print($"{Time[0]:t} 🟢 Manual fill detected — bracket locked");
			}
			
			// Step 0, handle daily series for prior H/L
			if(BarsInProgress == 1) 
			{
				if(CurrentBar > 1)
				{
					PrevDayHigh = Highs[1][1];
					PrevDayLow  = Lows[1][1];
					
            		RemoveDrawObject("PD_High");
            		RemoveDrawObject("PD_Low");
					
					// Previous day high
            		Draw.HorizontalLine(this, "PD_High", PrevDayHigh, Brushes.DodgerBlue, DashStyleHelper.Dash, 2, true);
					Draw.Text(this, "PD_High_Label", "Previous Day High", 0, PrevDayHigh + TickSize * 10, Brushes.DodgerBlue);
					// Below is for previous day low
            		Draw.HorizontalLine(this, "PD_Low", PrevDayLow,  Brushes.MediumBlue, DashStyleHelper.Dash, 2, true);
					Draw.Text(this, "PD_Low_Label", "Previous Day Low", 0, PrevDayLow - TickSize * 10, Brushes.MediumBlue);
					
				}
				return;
			}
			
			// Guard the 5-minute series
			// Dont start any logic until you've got at least 20 bars
			if (BarsInProgress != 0 || CurrentBar < BarsRequiredToTrade) 
				return;
			
			// make sure auxiliary series have enough history
			if (BarsArray[2].Count < 9 || CurrentBar < 6)   // 1-minute data for EMA, Volume[5]
			    return;

			// saves the current time on 'now' variable on every bar update
			DateTime now = Time[0];
			
			// 1) Session detection and reset
			// London start
			// When bar closes on specified time, session is detected
			if(now.Hour == Session1Hour && now.Minute == Session1Min)
			{
				Print("London open detected at " + now);
				SessionOpen = now;
				IsLondonSession = true;
				SessionTradeTaken = false; // So it resets in case it's still true from previous session
				breakEvenMoved = false;
				
				// Clear any old drawings
				BoxHigh = 0;
				BoxLow = 0;
				BoxHeight = 0;
				basicFiltersPassed = allFiltersPassed = bracketLocked = false;
				
				// remove everything to clean up working space and prevent spam when backtesting
	        	foreach (var tag in new[] { "BoxHigh", "BoxLow", "EntryLine", "StopLine", "TargetLine" })
	            RemoveDrawObject(tag);
				PlaySound(NinjaTrader.Core.Globals.InstallDir + @"\sounds\Alert4.wav");
			}
			
			
			// New York Start
			else if(now.Hour == Session2Hour && now.Minute == Session2Min) 
			{
				Print("New York open detected at " + now);
				SessionOpen        = now;
		        IsLondonSession    = false;
		        SessionTradeTaken  = false; // So it resets in case it's still true from previous session
				breakEvenMoved = false;
		        BoxHigh = 0;
		        BoxLow  = 0;
				BoxHeight = 0;
				basicFiltersPassed = allFiltersPassed = bracketLocked = false;
		        // remove everything to clean up working space and prevent spam when backtesting
        		foreach (var tag in new[] { "BoxHigh", "BoxLow", "EntryLine", "StopLine", "TargetLine" })
            	RemoveDrawObject(tag);
				PlaySound(NinjaTrader.Core.Globals.InstallDir + @"\sounds\Alert4.wav");
			}
			
			
			
			// only run ORB logic between 5 min and 55 mins after session open. 
			if(SessionOpen == default(DateTime) || now < SessionOpen.AddMinutes(5)) {
				return;
			}
			
			// define sessionEnd = open + 55 min
		    DateTime sessionEnd = SessionOpen.AddMinutes(55);
		
		    // Clear stuff and exit if it's past 55 minutes of session end.
		    if (now > sessionEnd)
		    {
		        foreach (var tag in new[] { "BoxHigh","BoxLow","EntryLine","StopLine","TargetLine", "TP_Label", "SL_Label" })
		            RemoveDrawObject(tag);
		        return;
		    }
			
			// 2) Five minutes after session open -> define the box
			if(SessionOpen != default(DateTime) && now == SessionOpen.AddMinutes(5)) 
			{
				Print("Drawing ORB box at " + now + $"  High={High[0]} Low={Low[0]}");
				// Gets the high and low. High[int barsAgo]
				BoxHigh = High[0];
				BoxLow = Low[0];
				BoxHeight = BoxHigh - BoxLow; // Calculates box height only after 5-min candle has occurred
				
				Print($"Drawing ORB box at {now}  High={BoxHigh:F2} Low={BoxLow:F2}");
				PlaySound(NinjaTrader.Core.Globals.InstallDir + @"\sounds\Alert2.wav");
				
				// Now draw the horizontal lines on your chart
				Draw.HorizontalLine(this, "BoxHigh", BoxHigh, Brushes.Crimson, DashStyleHelper.Solid, 2, true);
				Draw.HorizontalLine(this, "BoxLow", BoxLow, Brushes.Crimson, DashStyleHelper.Solid, 2, true);
			}
			
				// To clean up alerts/print statements between different candle iterations
				Alert("New Iteration", Priority.Low, "New iteration", "", 1, Brushes.Gray, Brushes.Transparent);
				Print("--------------------------------------------------------------");
			
			// 3) Check if the candles meet the conditions for a valid breakout ONLY after first 5-min has been drawn out
			// Keep checking as long as trade hasn't taken place. If trade is taken, bracket code and calculations don't run
			if(!SessionTradeTaken && BoxHeight > 0) 
			{
				// Step 1, breakout detection. most efficient step first before checking for more.
				bool longBreakout = Close[0] > BoxHigh;
				bool shortBreakout = Close[0] < BoxLow;
				if (!longBreakout && !shortBreakout)
            		return; // no breakout so keep waiting.
				
				// Step 2, check if the box size is valid or else skip the trade
				double minBox = IsLondonSession ? MinBoxLondon : MinBoxNewYork; // find min box required based on which session it is
				if(BoxHeight < minBox || BoxHeight > MaxBox) 
				{
					Print($"{Time[0]:t} ✖️ BoxHeight {BoxHeight:F2} invalid (needs {minBox:F2}–{MaxBox:F2})");
					return; // skip trade if box height is invalid
				}
				Print($"{Time[0]:t} ✔️ BoxHeight OK: {BoxHeight:F2}");
				
				
				// Step 3, confirm the EMAs
				double ema9 = EMA(Closes[2], 9)[0]; // Syntax: Closes[int barSeriesIndex][int barsAgo]
				double ema21 = EMA(Closes[2], 21)[0]; // barSeriesIndex = 2 → your 1-min series
				bool emaOKLong = Close[0] > ema9 && ema9 > ema21;
				bool emaOKShort = Close[0] < ema9 && ema9 < ema21;
				
				// Skip trades if EMAs do not fit the rules
				if(longBreakout && !emaOKLong) 
				{
					Print($"{Time[0]:t} ✖️ EMA misaligned LONG: Close={Close[0]:F2}, 9EMA={ema9:F2}, 21EMA={ema21:F2}");
                    return;
				}
				
				if (shortBreakout && !emaOKShort)
                {
                    Print($"{Time[0]:t} ✖️ EMA misaligned SHORT: Close={Close[0]:F2}, 9EMA={ema9:F2}, 21EMA={ema21:F2}");
                    return;
                }
                Print("{Time[0]:t} ✔️ EMA alignment OK"); // EMA fit rules so continue
				basicFiltersPassed = true; // after checking EMA
				
				
				// Step 4, volume filter with alert
				if(CurrentBar < 6)
					return;
				double volAvg = 0;
				for (int i = 1; i <=5; i++) {
					volAvg += Volume[i];
				}
				volAvg /= 5;
				double volThresh = volAvg * (IsLondonSession ? VolMultiplierLon : VolMultiplierNY);
				
				if(Volume[0] < volThresh) 
				{
					Print($"{Time[0]:t} ⚠️ Low volume: {Volume[0]} vs threshold {volThresh:F0}");
					Alert("LowVol", Priority.High, "Low volume breakout", "Alert2.wav", 0, Brushes.Orange, Brushes.Black);
				}
				
				else 
				{
					Print($"{Time[0]:t} ✔️ Volume OK: {Volume[0]} vs {volThresh:F0}");
				}
				
				
				// Step 5, provide support/resistance context alert about previous day high/low
				// Find target profit depending on if it's a long or short
				double target = longBreakout ? Close[0] + Math.Min(BoxHeight * 2, 4) : Close[0] - Math.Min(BoxHeight * 2, 4);
				// Checks if near any major level, regardless of direction
				double distHigh = Math.Abs(target - PrevDayHigh);
				double distLow  = Math.Abs(target - PrevDayLow);
				if(distHigh <= 2 || distLow <= 2)
				{
					double nearest = Math.Min(distHigh, distLow);
					Print($"{Time[0]:t} ⚠️ TP near prior H/L by {nearest:F2} points");
            		Alert("SRNear", Priority.Medium, "TP vs prior-day S/R", "", 1, Brushes.Yellow, Brushes.Black);
				}
				
		        else
				{
		            Print("{Time[0]:t} ✔️ Context OK");
					allFiltersPassed = true;
				}
				
				
				//if(basicFiltersPassed && !bracketLocked) 
				//{
				// Step 6, draw discretionary bracket, wait for real fill to lock trading ability
				double entryPrice = Close[0];
				double stopPrice  = longBreakout ? BoxLow - 0.25 : BoxHigh + 0.25;
				double targetPrice = target;
				
				// Draw the full width lines to use for manual bracket placement
				Draw.HorizontalLine(this, "EntryLine", entryPrice, Brushes.LimeGreen, DashStyleHelper.Solid, 3, true);
				Draw.HorizontalLine(this, "StopLine",   stopPrice,    Brushes.OrangeRed,   DashStyleHelper.Solid, 3, true);
				Draw.Text(this, "SL_Label", "Stop Loss", 0, stopPrice - TickSize * 10, Brushes.OrangeRed);
				Draw.HorizontalLine(this, "TargetLine", targetPrice,  Brushes.Lime, DashStyleHelper.Solid, 3, true);
				Draw.Text(this, "TP_Label", "Take Profit", 0, targetPrice + TickSize * 10, Brushes.Lime);
					
				// predicted entry price if you pulled it off perfectly
				filledEntryPrice = entryPrice; // should be updated when order is placed
				
				filledStopPrice = stopPrice;

				Print($"{Time[0]:t} 🥊 Bracket ready: Entry={entryPrice:F2}, SL={stopPrice:F2}, TP={targetPrice:F2}");
				Alert("BracketReady", Priority.High, 
				      "ORB bracket drawn – click LMT then entry line", 
				      "Alert3.wav", 1, Brushes.LimeGreen, Brushes.Black);
				PlaySound(NinjaTrader.Core.Globals.InstallDir + @"\sounds\Alert1.wav");
			}
			
			
			// Step 7, break-even alert once you've filled and locked. Will also stop updating bracket since trade filled
			// Doing it on 5 minute bar close as researched which is best for now. 
			if(SessionTradeTaken && bracketLocked && !breakEvenMoved) {
				double risk         = Math.Abs(filledEntryPrice - filledStopPrice);
				// Uses the actual filledEntryPrice captured when the order was executed
				// Computes risk based on when you entered
		        double beTriggerLong  = filledEntryPrice + 1.5 * risk;
		        double beTriggerShort = filledEntryPrice - 1.5 * risk;
		
		        if (Position.MarketPosition == MarketPosition.Long  && Close[0] >= beTriggerLong ||
		            Position.MarketPosition == MarketPosition.Short && Close[0] <= beTriggerShort)
		        {
		            Print($"{now:t} ⚙ +1.5R hit → move SL to BE");
		            Alert("MoveBE", Priority.High, "Move stop to breakeven", "Alert4.wav", 0, Brushes.White, Brushes.DarkBlue);
					PlaySound(NinjaTrader.Core.Globals.InstallDir + @"\sounds\Alert1.wav");
					PlaySound(NinjaTrader.Core.Globals.InstallDir + @"\sounds\Alert1.wav");
					
					// Redraw StopLine at breakeven
			        RemoveDrawObject("StopLine");  // Remove old SL
			        Draw.HorizontalLine(this, "StopLine", filledEntryPrice, Brushes.OrangeRed, DashStyleHelper.Solid, 3, true);
			        Draw.Text(this, "SL_Label", "Stop Loss (BE)", 0, filledEntryPrice - TickSize * 10, Brushes.OrangeRed);
					
		            breakEvenMoved = true;
		        }
			}
			
			
		}
		
		// Lock yourself out from further trades once you actually fill
		protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity,
		    MarketPosition marketPosition, string orderId, DateTime time)
			{
				// If not flat, you took long or short
			    if (execution.Instrument == Instrument && !SessionTradeTaken && marketPosition != MarketPosition.Flat)
			    {
			        SessionTradeTaken = true;
					breakEvenMoved = false; // reset for this trade
			        Print($"✅ Fill detected at {price:F2} ({marketPosition}), locking out further brackets for this session.");
			    }
			}

		#region Properties
		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name="Session1Hour", Description="London open hour (CT)", Order=1, GroupName="Parameters")]
		public int Session1Hour
		{ get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name="Session1Min", Description="London open minute", Order=2, GroupName="Parameters")]
		public int Session1Min
		{ get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name="Session2Hour", Description="New York open hour (CT)", Order=3, GroupName="Parameters")]
		public int Session2Hour
		{ get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name="Session2Min", Description="New York Open Minute", Order=4, GroupName="Parameters")]
		public int Session2Min
		{ get; set; }

		[NinjaScriptProperty]
		[Range(0, double.MaxValue)]
		[Display(Name="MinBoxLondon", Description="Minimum box height for London (pts)", Order=5, GroupName="Parameters")]
		public double MinBoxLondon
		{ get; set; }

		[NinjaScriptProperty]
		[Range(0, double.MaxValue)]
		[Display(Name="MinBoxNewYork", Description="Minimum box height for NY (pts)", Order=6, GroupName="Parameters")]
		public double MinBoxNewYork
		{ get; set; }

		[NinjaScriptProperty]
		[Range(0, double.MaxValue)]
		[Display(Name="MaxBox", Description="Maximum box height for both sessions", Order=7, GroupName="Parameters")]
		public double MaxBox
		{ get; set; }

		[NinjaScriptProperty]
		[Range(0, double.MaxValue)]
		[Display(Name="VolMultiplierLon", Description="Volume multiple required in London", Order=8, GroupName="Parameters")]
		public double VolMultiplierLon
		{ get; set; }

		[NinjaScriptProperty]
		[Range(0, double.MaxValue)]
		[Display(Name="VolMultiplierNY", Description="Volume multiple required in New York", Order=9, GroupName="Parameters")]
		public double VolMultiplierNY
		{ get; set; }
		#endregion

	}
}
