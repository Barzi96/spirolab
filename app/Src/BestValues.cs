using System;
using System.Collections.Generic;
using System.Globalization;

namespace SpiroLab
{
    // Source categories are deliberately explicit. A software reconstruction is
    // never relabelled as a device value, and a legacy value is never presented as
    // a newly verified USB measurement.
    public enum MeasurementSource
    {
        Unavailable = 0,
        DeviceReported = 1,
        SoftwareCalculated = 2,
        LegacyUnverified = 3,
        Derived = 4
    }

    public static class Metric
    {
        public const string Fvc="FVC",Fev1="FEV1",Fev1Fvc="FEV1/FVC",Pef="PEF",Fet="FET";
        public const string Fef25="FEF25",Fef50="FEF50",Fef75="FEF75",Fef2575="FEF25-75";
        public const string Fev3="FEV3",Fev6="FEV6",Bev="BEV";
        public static string Unit(string name)
        {
            switch(name)
            {
                case Fvc: case Fev1: case Fev3: case Fev6: return "L";
                case Fev1Fvc: return "%";
                case Pef: case Fef25: case Fef50: case Fef75: case Fef2575: return "L/s";
                case Fet: return "s";
                case Bev: return "mL";
                default: return "";
            }
        }
        public static string DisplayName(string name)
        {
            return name==Fef2575?"FEF25–75":name==Fev1Fvc?"FEV1/FVC":name==Bev?"BEV (estimate)":name;
        }
    }

    public sealed class MeasuredValue
    {
        public string Name="";
        public string Unit="";
        public double? Value;
        public string SourceManeuverId="";
        public int AcquisitionSequence;
        public MeasurementSource Source;
        public string ValidationStatus="unavailable";
        public double? Resolution;
        public string MissingReason="";
        public string Method="";
        public double? Predicted;
        public double? PercentPredicted;

        public bool Available { get { return Value.HasValue; } }
        public bool DeviceReported { get { return Source==MeasurementSource.DeviceReported; } }
        public bool SoftwareCalculated { get { return Source==MeasurementSource.SoftwareCalculated || Source==MeasurementSource.Derived; } }
        public bool LegacyUnverified { get { return Source==MeasurementSource.LegacyUnverified; } }

        public string SourceCode
        {
            get
            {
                switch(Source)
                {
                    case MeasurementSource.DeviceReported: return "device_reported";
                    case MeasurementSource.SoftwareCalculated: return "software_calculated";
                    case MeasurementSource.LegacyUnverified: return "legacy_unverified";
                    case MeasurementSource.Derived: return "derived_from_selected_measurements";
                    default: return "unavailable";
                }
            }
        }

        public MeasuredValue Clone()
        {
            return (MeasuredValue)MemberwiseClone();
        }

        public override string ToString()
        {
            return Name+"="+(Value.HasValue?Value.Value.ToString("R",CultureInfo.InvariantCulture):"unavailable")+" "+Unit+" ("+SourceCode+")";
        }
    }

    // Per-maneuver measurement set. Device values win only when the result block
    // can be bound to this exact completed capture; reconstructed values stay
    // available separately for diagnostics and parity comparison.
    public sealed class ManeuverMeasurementSet
    {
        readonly Dictionary<string,MeasuredValue> values=new Dictionary<string,MeasuredValue>(StringComparer.Ordinal);
        public Trial Trial {get;private set;}
        public ManeuverMeasurementSet Reconstructed {get;private set;}
        public MeasuredValue Fvc {get{return Get(Metric.Fvc);}}
        public MeasuredValue Fev1 {get{return Get(Metric.Fev1);}}
        public MeasuredValue Fev1Fvc {get{return Get(Metric.Fev1Fvc);}}
        public MeasuredValue Pef {get{return Get(Metric.Pef);}}
        public MeasuredValue Fet {get{return Get(Metric.Fet);}}
        public MeasuredValue Fef25 {get{return Get(Metric.Fef25);}}
        public MeasuredValue Fef50 {get{return Get(Metric.Fef50);}}
        public MeasuredValue Fef75 {get{return Get(Metric.Fef75);}}
        public MeasuredValue Fef2575 {get{return Get(Metric.Fef2575);}}
        public MeasuredValue Fev3 {get{return Get(Metric.Fev3);}}
        public MeasuredValue Fev6 {get{return Get(Metric.Fev6);}}
        public IEnumerable<MeasuredValue> All {get{return values.Values;}}

        public MeasuredValue Get(string name)
        {
            MeasuredValue value;
            return name!=null && values.TryGetValue(name,out value)?value:Unavailable(null,name,"unknown_metric");
        }

        public static ManeuverMeasurementSet Build(Trial trial)
        {
            ManeuverMeasurementSet software=BuildCore(trial,false);
            ManeuverMeasurementSet set=BuildCore(trial,true);
            set.Reconstructed=software;
            return set;
        }

        static ManeuverMeasurementSet BuildCore(Trial trial,bool allowDevice)
        {
            ManeuverMeasurementSet set=new ManeuverMeasurementSet(); set.Trial=trial;
            if(trial==null) return set;
            AnalysisResult r=trial.Result;
            Reference reference=r==null?null:r.Ref;
            Device.DeviceResults device=allowDevice?trial.DeviceValues:null;
            bool verifiedDevice=allowDevice && VerifiedDevice(trial,device);
            bool deviceOnlyCore=allowDevice && trial.RequiresDeviceCore && trial.DeviceCaptureSequence>0 && !trial.Simulated && !trial.Imported;
            bool legacyDevice=allowDevice && !verifiedDevice && !deviceOnlyCore && device!=null && device.Present && !device.Associated && device.Raw!=null && HasAnyDeviceValue(device);
            // A new real USB maneuver must have all four device core fields. The
            // software analyzer remains available only for the curve-derived
            // parameters and for the separate Reconstructed diagnostics object.

            // Device-reported fields for which the observed protocol supplies values.
            if(verifiedDevice)
            {
                int core=device.CoreOffset;
                set.Add(DeviceValue(trial,device,Metric.Fvc,device.FvcValid?device.Fvc:(double?)null,0.01,"cod_LAST@0x"+core.ToString("X2")+" observed_u16_0.01L",reference==null?null:(double?)Predicted(reference,Metric.Fvc)));
                set.Add(DeviceValue(trial,device,Metric.Fev1,device.Fev1Valid?device.Fev1:(double?)null,0.01,"cod_LAST@0x"+(core+2).ToString("X2")+" observed_u16_0.01L",reference==null?null:(double?)Predicted(reference,Metric.Fev1)));
                set.Add(DeviceValue(trial,device,Metric.Pef,device.PefValid?device.Pef:(double?)null,0.01,"cod_LAST@0x"+(core+4).ToString("X2")+" observed_u16_0.01L/s",reference==null?null:(double?)Predicted(reference,Metric.Pef)));
                set.Add(DeviceValue(trial,device,Metric.Fet,device.FetValid?device.Fet:(double?)null,0.01,"cod_LAST@0x"+(core+10).ToString("X2")+" observed_u16_0.01s",null));
            }
            else if(legacyDevice)
            {
                set.Add(LegacyDeviceValue(trial,device,Metric.Fvc,device.Fvc,r,reference));
                set.Add(LegacyDeviceValue(trial,device,Metric.Fev1,device.Fev1,r,reference));
                set.Add(LegacyDeviceValue(trial,device,Metric.Pef,device.Pef,r,reference));
                set.Add(LegacyDeviceValue(trial,device,Metric.Fet,device.Fet,r,null));
            }

            bool valid=r!=null && r.Valid;
            // Missing device fields may use a software value, but its source stays explicit.
            if(!set.Has(Metric.Fvc)) set.Add(deviceOnlyCore?Unavailable(trial,Metric.Fvc,"device_core_result_required_for_new_real_maneuver"):Software(trial,r,Metric.Fvc,valid && r.Fvc>0 && Present(r,r.FvcAvailable,r.Fvc)?r.Fvc:(double?)null,0.01,r!=null&&r.FvcAvailable?"reconstructed_expiration_volume":"legacy_numeric_result_no_availability_metadata",reference));
            if(!set.Has(Metric.Fev1)) set.Add(deviceOnlyCore?Unavailable(trial,Metric.Fev1,"device_core_result_required_for_new_real_maneuver"):Software(trial,r,Metric.Fev1,valid && r.Fev1>0 && r.Fev1<=r.Fvc+.001 && Present(r,r.Fev1Available,r.Fev1)?r.Fev1:(double?)null,0.01,r!=null&&r.Fev1Available?"reconstructed_volume_at_1s":"legacy_numeric_result_no_availability_metadata",reference));
            if(!set.Has(Metric.Pef)) set.Add(deviceOnlyCore?Unavailable(trial,Metric.Pef,"device_core_result_required_for_new_real_maneuver"):Software(trial,r,Metric.Pef,valid && r.Pef>0 && Present(r,r.PefAvailable,r.Pef)?r.Pef:(double?)null,0.01,r!=null&&r.PefAvailable?"reconstructed_peak_flow":"legacy_numeric_result_no_availability_metadata",reference));
            if(!set.Has(Metric.Fet)) set.Add(deviceOnlyCore?Unavailable(trial,Metric.Fet,"device_core_result_required_for_new_real_maneuver"):Software(trial,r,Metric.Fet,valid && r.Fet>0 && Present(r,r.FetAvailable,r.Fet)?r.Fet:(double?)null,0.01,r!=null&&r.FetAvailable?"reconstructed_expiration_duration":"legacy_numeric_result_no_availability_metadata",null));

            set.Add(Software(trial,r,Metric.Fef25,valid && r.Fef25>0 && Present(r,r.Fef25Available,r.Fef25)?r.Fef25:(double?)null,0.01,r!=null&&r.Fef25Available?"reconstructed_flow_at_25pct_fvc":"legacy_numeric_result_no_availability_metadata",reference));
            set.Add(Software(trial,r,Metric.Fef50,valid && r.Fef50>0 && Present(r,r.Fef50Available,r.Fef50)?r.Fef50:(double?)null,0.01,r!=null&&r.Fef50Available?"reconstructed_flow_at_50pct_fvc":"legacy_numeric_result_no_availability_metadata",reference));
            set.Add(Software(trial,r,Metric.Fef75,valid && r.Fef75>0 && Present(r,r.Fef75Available,r.Fef75)?r.Fef75:(double?)null,0.01,r!=null&&r.Fef75Available?"reconstructed_flow_at_75pct_fvc":"legacy_numeric_result_no_availability_metadata",reference));
            set.Add(Software(trial,r,Metric.Fef2575,valid && r.Fef2575>0 && Present(r,r.Fef2575Available,r.Fef2575)?r.Fef2575:(double?)null,0.01,r!=null&&r.Fef2575Available?"reconstructed_mid_expiratory_flow_from_time_volume_span":"legacy_numeric_result_no_availability_metadata",reference));
            set.Add(Software(trial,r,Metric.Fev3,valid && r.Fev3>0 && r.Fev3Available?r.Fev3:(double?)null,0.01,"reconstructed_volume_at_3s",null));
            set.Add(Software(trial,r,Metric.Fev6,valid && r.Fev6>0 && r.Fev6Available?r.Fev6:(double?)null,0.01,"reconstructed_volume_at_6s",null));

            // The ratio is derived from the final per-maneuver FVC and FEV1, never
            // from a separately selected numeric field.
            MeasuredValue fvc=set.Fvc,fev1=set.Fev1;
            if(fvc.Available && fev1.Available && fvc.Value.Value>0 && fev1.Value.Value<=fvc.Value.Value+.05)
            {
                MeasuredValue ratio=Base(trial,Metric.Fev1Fvc,fev1.Value.Value/fvc.Value.Value*100,reference,Metric.Fev1Fvc);
                ratio.Source=MeasurementSource.Derived;
                ratio.ValidationStatus="derived_unverified";
                ratio.Method="fev1_div_fvc_from_same_maneuver";
                ratio.MissingReason="";
                set.values[Metric.Fev1Fvc]=ratio;
            }
            else set.Add(Unavailable(trial,Metric.Fev1Fvc,"FVC یا FEV1 قابل استفاده در این مانور موجود نیست."));

            if(r!=null && r.BevAvailable && r.Bev>0)
            {
                MeasuredValue bev=Base(trial,Metric.Bev,r.Bev*1000.0,null,Metric.Bev);
                bev.Source=MeasurementSource.SoftwareCalculated; bev.ValidationStatus="software_calculated_unverified";
                bev.Resolution=1; bev.Method="volume_at_peak_tangent_time_zero"; set.values[Metric.Bev]=bev;
            }
            else set.Add(Unavailable(trial,Metric.Bev,"حجم پس‌برون‌یابی در دسترس نیست."));
            return set;
        }

        bool Has(string name) { return values.ContainsKey(name) && values[name].Available; }

        void Add(MeasuredValue value) { if(value!=null && value.Name.Length>0) values[value.Name]=value; }

        static MeasuredValue Base(Trial trial,string name,double? value,Reference reference,string predictedMetric)
        {
            MeasuredValue m=new MeasuredValue {Name=name,Unit=Metric.Unit(name),Value=value,SourceManeuverId=trial.ManeuverId,AcquisitionSequence=trial.AcquisitionSequence};
            if(reference!=null && value.HasValue)
            {
                double predicted=Predicted(reference,predictedMetric);
                if(predicted>0) {m.Predicted=predicted;m.PercentPredicted=value.Value/predicted*100.0;}
            }
            return m;
        }

        static bool Present(AnalysisResult r,bool available,double value)
        {
            // Legacy persisted results have no availability metadata; only their
            // numeric field can be used. Modern results must honor the explicit flag.
            return value>0 && (r==null || !r.AvailabilityMetadata || available);
        }

        static MeasuredValue Software(Trial trial,AnalysisResult r,string name,double? value,double? resolution,string method,Reference reference)
        {
            if(!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value) || value.Value<=0)
                return Unavailable(trial,name,MissingReason(trial,r,name));
            MeasuredValue m=Base(trial,name,value,reference,name);
            m.Source=MeasurementSource.SoftwareCalculated;
            m.ValidationStatus="software_calculated_unverified";
            m.Resolution=resolution; m.Method=method; m.MissingReason="";
            return m;
        }

        static MeasuredValue DeviceValue(Trial trial,Device.DeviceResults device,string name,double? value,double? resolution,string method,double? predicted)
        {
            if(!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value) || value.Value<=0)
                return Unavailable(trial,name,"device_field_invalid_or_unavailable");
            MeasuredValue m=new MeasuredValue {Name=name,Unit=Metric.Unit(name),Value=value,SourceManeuverId=trial.ManeuverId,AcquisitionSequence=trial.AcquisitionSequence,Source=MeasurementSource.DeviceReported,ValidationStatus="verified_capture_association",Resolution=resolution,Method=method,Predicted=predicted};
            if(predicted.HasValue && predicted.Value>0) m.PercentPredicted=value.Value/predicted.Value*100.0;
            return m;
        }

        static MeasuredValue LegacyDeviceValue(Trial trial,Device.DeviceResults device,string name,double value,AnalysisResult r,Reference reference)
        {
            if(!(value>0) || double.IsNaN(value) || double.IsInfinity(value)) return Unavailable(trial,name,"legacy_device_value_invalid");
            MeasuredValue m=Base(trial,name,value,reference,name);
            m.Source=MeasurementSource.LegacyUnverified;
            m.ValidationStatus="legacy_device_block_unverified_association";
            m.Resolution=0.01; m.Method="persisted_device_block_without_capture_token"; m.MissingReason="";
            return m;
        }

        static MeasuredValue Unavailable(Trial trial,string name,string reason)
        {
            return new MeasuredValue {Name=name,Unit=Metric.Unit(name),Value=null,SourceManeuverId=trial==null?"":trial.ManeuverId,AcquisitionSequence=trial==null?0:trial.AcquisitionSequence,Source=MeasurementSource.Unavailable,ValidationStatus="unavailable",MissingReason=reason??"unavailable",Method=""};
        }

        static string MissingReason(Trial trial,AnalysisResult r,string name)
        {
            if(trial==null) return "maneuver_missing";
            if(trial.OperatorRejected) return "operator_rejected";
            if(!trial.Completed && !trial.OperatorStopped) return "capture_incomplete";
            if(r==null) return "analysis_missing";
            if(!r.Valid) return string.IsNullOrEmpty(r.InvalidReason)?"analysis_invalid":r.InvalidReason;
            if(name==Metric.Fef2575 && !r.Fef2575Available) return string.IsNullOrEmpty(r.CurveProblem)?"adequate_time_volume_span_unavailable":r.CurveProblem;
            if(name==Metric.Fev1 && r.Fet>0 && r.Fet<1) return "expiration_shorter_than_1s";
            return "measurement_unavailable";
        }

        static bool VerifiedDevice(Trial trial,Device.DeviceResults d)
        {
            return trial!=null && d!=null && d.Present && d.BlockValid && d.Associated && d.Raw!=null &&
                trial.DeviceCaptureSequence>0 && d.CaptureSequence==trial.DeviceCaptureSequence &&
                (d.SerialNumber==trial.DeviceSerial || (d.SerialNumber.Length==0 && trial.DeviceSerial.Length==0)) &&
                !trial.Simulated && !trial.Imported;
        }

        static bool HasAnyDeviceValue(Device.DeviceResults d)
        {
            return d!=null && (d.Fvc>0 || d.Fev1>0 || d.Pef>0 || d.Fet>0);
        }

        static double Predicted(Reference r,string metric)
        {
            if(r==null) return 0;
            switch(metric)
            {
                case Metric.Fvc:return r.Fvc;
                case Metric.Fev1:return r.Fev1;
                case Metric.Fev1Fvc:return r.Fev1Fvc;
                case Metric.Pef:return r.Pef;
                case Metric.Fef25:return r.Fef25;
                case Metric.Fef50:return r.Fef50;
                case Metric.Fef75:return r.Fef75;
                case Metric.Fef2575:return r.Fef2575;
                default:return 0;
            }
        }
    }
}
