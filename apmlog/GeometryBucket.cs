using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.IO.Ports;
using System.Threading;


using System.Net;

using System.Xml; // config file
using System.Runtime.InteropServices; // dll imports

using System.Reflection;

using System.IO;

using System.Drawing.Drawing2D;

namespace apmgeometry
{
    /// <summary>
    /// Struct as used in Ardupilot
    /// </summary>
    public struct Locationwp
    {
        public byte id;				// command id
        public byte options;
        public float p1;				// param 1
        public float p2;				// param 2
        public float p3;				// param 3
        public float p4;				// param 4
        public double lat;				// Lattitude * 10**7
        public double lng;				// Longitude * 10**7
        public float alt;				// Altitude in centimeters (meters * 100)
    };


	//Removed view kine GMap stuff

    public class PointLatLngAlt
    {
        public double Lat = 0;
        public double Lng = 0;
        public double Alt = 0;
        public string Tag = "";
        public Color color = Color.White;

        const float rad2deg = (float)(180 / Math.PI);
        const float deg2rad = (float)(1.0 / rad2deg);

        public PointLatLngAlt(double lat, double lng, double alt, string tag)
        {
            this.Lat = lat;
            this.Lng = lng;
            this.Alt = alt;
            this.Tag = tag;
        }

        public PointLatLngAlt()
        {

        }

//        public PointLatLngAlt(GMap.NET.PointLatLng pll)
//        {
//            this.Lat = pll.Lat;
//            this.Lng = pll.Lng;
//        }

        public PointLatLngAlt(Locationwp locwp)
        {
            this.Lat = locwp.lat;
            this.Lng = locwp.lng;
            this.Alt = locwp.alt;
        }

        public PointLatLngAlt(PointLatLngAlt plla)
        {
            this.Lat = plla.Lat;
            this.Lng = plla.Lng;
            this.Alt = plla.Alt;
            this.color = plla.color;
            this.Tag = plla.Tag;
        }

//        public PointLatLng Point()
//        {
//            return new PointLatLng(Lat, Lng);
//        }
//
        public override bool Equals(Object pllaobj)
         {
             PointLatLngAlt plla = (PointLatLngAlt)pllaobj;

             if (plla == null)
                 return false;

             if (this.Lat == plla.Lat &&
             this.Lng == plla.Lng &&
             this.Alt == plla.Alt &&
             this.color == plla.color &&
             this.Tag == plla.Tag)
             {
                 return true;
             }
             return false;
         }

        public override int GetHashCode()
        {
            return (int)((Lat + Lng + Alt) * 100);
        }

        public override string ToString()
        {
            return Lat + "," + Lng + "," + Alt;
        }

        /// <summary>
        /// Calc Distance in M
        /// </summary>
        /// <param name="p2"></param>
        /// <returns>Distance in M</returns>
        public double GetDistance(PointLatLngAlt p2)
        {
            double d = Lat * 0.017453292519943295;
            double num2 = Lng * 0.017453292519943295;
            double num3 = p2.Lat * 0.017453292519943295;
            double num4 = p2.Lng * 0.017453292519943295;
            double num5 = num4 - num2;
            double num6 = num3 - d;
            double num7 = Math.Pow(Math.Sin(num6 / 2.0), 2.0) + ((Math.Cos(d) * Math.Cos(num3)) * Math.Pow(Math.Sin(num5 / 2.0), 2.0));
            double num8 = 2.0 * Math.Atan2(Math.Sqrt(num7), Math.Sqrt(1.0 - num7));
            return (6371 * num8) * 1000.0; // M
        }

        public double GetDistance2(PointLatLngAlt p2)
        {
            //http://www.movable-type.co.uk/scripts/latlong.html
            var R = 6371.0; // 6371 km
            var dLat = (p2.Lat - Lat) * deg2rad;
            var dLon = (p2.Lng - Lng) * deg2rad;
            var lat1 = Lat * deg2rad;
            var lat2 = p2.Lat * deg2rad;

            var a = Math.Sin(dLat / 2.0) * Math.Sin(dLat / 2.0) +
                    Math.Sin(dLon / 2.0) * Math.Sin(dLon / 2.0) * Math.Cos(lat1) * Math.Cos(lat2);
            var c = 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a));
            var d = R * c * 1000.0; // M

            return d;
        }
    }



    public class Common
    {
        public enum distances
        {
            Meters,
            Feet
        }

        public enum speeds
        {
            ms,
            fps,
            kph,
            mph,
            knots
        }


        /// <summary>
        /// from libraries\AP_Math\rotations.h
        /// </summary>
        public enum Rotation
        {
            ROTATION_NONE = 0,
            ROTATION_YAW_45,
            ROTATION_YAW_90,
            ROTATION_YAW_135,
            ROTATION_YAW_180,
            ROTATION_YAW_225,
            ROTATION_YAW_270,
            ROTATION_YAW_315,
            ROTATION_ROLL_180,
            ROTATION_ROLL_180_YAW_45,
            ROTATION_ROLL_180_YAW_90,
            ROTATION_ROLL_180_YAW_135,
            ROTATION_PITCH_180,
            ROTATION_ROLL_180_YAW_225,
            ROTATION_ROLL_180_YAW_270,
            ROTATION_ROLL_180_YAW_315,
            ROTATION_ROLL_90,
            ROTATION_ROLL_90_YAW_45,
            ROTATION_ROLL_90_YAW_90,
            ROTATION_ROLL_90_YAW_135,
            ROTATION_ROLL_270,
            ROTATION_ROLL_270_YAW_45,
            ROTATION_ROLL_270_YAW_90,
            ROTATION_ROLL_270_YAW_135,
            ROTATION_PITCH_90,
            ROTATION_PITCH_270,
            ROTATION_MAX
        }


   

        public enum ac2ch6modes
        {
            // CH_6 Tuning
            // -----------
            CH6_0NONE = 0,  // no tuning performed
            CH6_STABILIZE_KP = 1,   // stabilize roll/pitch angle controller's P term
            CH6_STABILIZE_KI = 2,   // stabilize roll/pitch angle controller's I term
            CH6_STABILIZE_KD = 29,   // stabilize roll/pitch angle controller's D term
            CH6_YAW_KP = 3,   // stabilize yaw heading controller's P term
            CH6_YAW_KI = 24,   // stabilize yaw heading controller's P term
            CH6_ACRO_KP = 25,   // acro controller's P term.  converts pilot input to a desired roll, pitch or yaw rate
            CH6_RATE_KP = 4,   // body frame roll/pitch rate controller's P term
            CH6_RATE_KI = 5,   // body frame roll/pitch rate controller's I term
            CH6_RATE_KD = 21,   // body frame roll/pitch rate controller's D term
            CH6_YAW_RATE_KP = 6,   // body frame yaw rate controller's P term
            CH6_YAW_RATE_KD = 26,   // body frame yaw rate controller's D term
            CH6_THR_HOLD_KP = 14,   // altitude hold controller's P term (alt error to desired rate)
            CH6_THROTTLE_KP = 7,   // throttle rate controller's P term (desired rate to acceleration or motor output)
            CH6_THROTTLE_KI = 33,   // throttle rate controller's I term (desired rate to acceleration or motor output)
            CH6_THR_ACCEL_KP = 34,   // accel based throttle controller's P term
            CH6_THR_ACCEL_KI = 35,   // accel based throttle controller's I term
            CH6_THR_ACCEL_KD = 36,   // accel based throttle controller's D term
            CH6_TOP_BOTTOM_RATIO = 8,   // upper/lower motor ratio (not used)
            CH6_RELAY = 9,   // switch relay on if ch6 high, off if low
            CH6_TRAVERSE_SPEED = 10,   // maximum speed to next way point (0 to 10m/s)
            CH6_NAV_KP = 11,   // navigation rate controller's P term (speed error to tilt angle)
            CH6_NAV_KI = 20,   // navigation rate controller's I term (speed error to tilt angle)
            CH6_LOITER_KP = 12,   // loiter distance controller's P term (position error to speed)
            CH6_LOITER_KI = 27,   // loiter distance controller's I term (position error to speed)
            CH6_HELI_EXTERNAL_GYRO = 13,   // TradHeli specific external tail gyro gain
            CH6_OPTFLOW_KP = 17,   // optical flow loiter controller's P term (position error to tilt angle)
            CH6_OPTFLOW_KI = 18,   // optical flow loiter controller's I term (position error to tilt angle)
            CH6_OPTFLOW_KD = 19,   // optical flow loiter controller's D term (position error to tilt angle)
            CH6_LOITER_RATE_KP = 22,   // loiter rate controller's P term (speed error to tilt angle)
            CH6_LOITER_RATE_KI = 28,   // loiter rate controller's I term (speed error to tilt angle)
            CH6_LOITER_RATE_KD = 23,   // loiter rate controller's D term (speed error to tilt angle)
            CH6_AHRS_YAW_KP = 30,   // ahrs's compass effect on yaw angle (0 = very low, 1 = very high)
            CH6_AHRS_KP = 31,   // accelerometer effect on roll/pitch angle (0=low)
            CH6_INAV_TC = 32,          // inertial navigation baro/accel and gps/accel time
            CH6_DECLINATION = 38,         // compass declination in radians
            CH6_CIRCLE_RATE = 39,        // circle turn rate in degrees (hard coded to about 45 degrees in either direction)
        }

	}




}
