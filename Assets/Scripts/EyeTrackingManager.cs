using UnityEngine;
using System;
using System.IO;
using System.Runtime.InteropServices;
using ViveSR.anipal.Eye;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.UI;
using System.Diagnostics;

namespace ViveSR.anipal.Eye
{
    public class EyeTrackingManager : MonoBehaviour
    {
        public static EyeTrackingManager instance;

        [SerializeField]
        private string subjectName = "Default Dave";
        private static EyeData eyeData = new EyeData();

        public static float pupilDiameterLeft;
        public static float pupilDiameterRight;

        private bool eye_callback_registered = false; // This should have a better interface

        private static int callbackCount = 0;
        private const int logEveryN = 10;
        private static string logPath;
        private static StreamWriter writer;
        private static Queue<string> logQueue = new Queue<string>();

        // Luminance stuff
        private bool luminanceEnabled = true;
        private bool luminanceCreated = false;
        private int luminanceWidth = 256;
        private int luminanceHeight = 144;
        private Camera luminanceCamera;
        private Texture2D luminanceTexture;
        private RenderTexture luminenceRenderTexture;
        public static float luminance = -1.0f;
        private float luminanceTime = -1.0f;
        private float luminanceRate = 1.0f / 10.0f;
        public GameObject calibrationScreen;
        private Camera cam;
        public float fearPeriod = 30.0f; //seconds
        private static int fearStart = -1;
        private static int fearMs;
        private static List<float> fearPupil;
        private static List<float> fearLuminance;
        private static List<float> fearIncrements;
        private static bool fearChecked = false;
        private static bool fearEnabled = false;
        private static bool fearConfigured = false;



        internal class MonoPInvokeCallbackAttribute : System.Attribute
        {
            public MonoPInvokeCallbackAttribute() { }
        }

        void Start()
        {
            if (instance == null)
            {
                instance = this;
            }
            cam = Camera.main;
            if (!SRanipal_Eye_Framework.Instance.EnableEye)
            {
                return;
            }

            string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");  // e.g., 2025-10-22_14-30-00
            string logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "AcroGenData",
                subjectName,
                sceneName,
                timestamp
            );
            Directory.CreateDirectory(logDirectory);
            string logPath = Path.Combine(logDirectory, "eye_tracking_log_test.csv");

            writer = new StreamWriter(logPath);
            writer.WriteLine("Timestamp,PupilDiameterLeft,PupilDiameterRight,GazeLeftX,GazeLeftY,GazeLeftZ,GazeRightX,GazeRightY,GazeRightZ,CalculatedFear"); // header row
            // Add CalculatedFear header
            writer.Flush();

            if (luminanceEnabled)
            {
                CreateLuminanceCamera();
            }
            // Would check the "workload server" here...
            fearChecked = true;

            fearMs = (int)Mathf.Round(fearPeriod * 1000);
            fearPupil = new List<float>();
            fearLuminance = new List<float>();


            SRanipal_Eye.WrapperRegisterEyeDataCallback(Marshal.GetFunctionPointerForDelegate((SRanipal_Eye.CallbackBasic)EyeCallback));
            UnityEngine.Debug.Log("EyeCallback registered and logging to: " + logPath);
        }

        void OnApplicationQuit()
        {
            if (writer != null)
            {
                writer.Flush();
                writer.Close();
            }
        }

        void Update()
        {
            if (SRanipal_Eye_Framework.Status != SRanipal_Eye_Framework.FrameworkStatus.WORKING &&
                SRanipal_Eye_Framework.Status != SRanipal_Eye_Framework.FrameworkStatus.NOT_SUPPORT)
            {
                return;
            }

            if (SRanipal_Eye_Framework.Instance.EnableEyeDataCallback && !eye_callback_registered)
            {
                SRanipal_Eye.WrapperRegisterEyeDataCallback(Marshal.GetFunctionPointerForDelegate((SRanipal_Eye.CallbackBasic)EyeCallback));
                UnityEngine.Debug.Log("EyeTrackingManager: EyeCallback registered (v1)");
                eye_callback_registered = true;
            }

            // Write any buffered data to file
            while (logQueue.Count > 0)
            {
                string line = logQueue.Dequeue();
                writer.WriteLine(line);
            }

            writer.Flush();
        }

        [MonoPInvokeCallback]
        private static void EyeCallback(ref EyeData eye_data)
        {
            if (callbackCount % logEveryN != 0) return;
            eyeData = eye_data; // need?

            var timestamp = eye_data.timestamp;
            var pupilLeft = eye_data.verbose_data.left.pupil_diameter_mm;
            var pupilRight = eye_data.verbose_data.right.pupil_diameter_mm;
            var gazeLeft = eye_data.verbose_data.left.gaze_direction_normalized;
            var gazeRight = eye_data.verbose_data.right.gaze_direction_normalized;

            float fear_result = Analysis(timestamp, pupilLeft, pupilRight, luminance);
            // Format a CSV line
            string line = $"{timestamp:F3},{pupilLeft},{pupilRight}," +
                        $"{gazeLeft.x},{gazeLeft.y},{gazeLeft.z}," +
                        $"{gazeRight.x},{gazeRight.y},{gazeRight.z}," +
                        $"{fear_result}";

            logQueue.Enqueue(line);

        }

        private void FixedUpdate()
        {
            if (luminanceEnabled && luminanceCreated && Time.time - luminanceTime >= luminanceRate)
            {
                EyeTrackingManager.luminance = GetLuminance(luminanceCamera.targetTexture);
                //Debug.LogFormat("ETM Luminance {0}", EyeTrackingManager.luminance);
                luminanceTime = Time.time;
            }
        }

        private float GetLuminance(RenderTexture renderTexture)
        {
            RenderTexture oldRenderTexture = RenderTexture.active;
            RenderTexture.active = renderTexture;

            Texture2D texture2D = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGBAFloat, false);
            texture2D.ReadPixels(new Rect(0, 0, texture2D.width, texture2D.height), 0, 0, false);
            texture2D.Apply();

            Color[] allColors = texture2D.GetPixels();

            float totalLuminance = 0f;

            foreach (Color color in allColors)
            {
                totalLuminance += (color.r * 0.2126f) + (color.g * 0.7152f) + (color.b * 0.0722f);
            }

            float averageLuminance = totalLuminance / allColors.Length;


            RenderTexture.active = oldRenderTexture;
            UnityEngine.Object.Destroy(texture2D);

            return averageLuminance;
        }

        private void CreateLuminanceCamera()
        {
            GameObject cameraChild = new GameObject();
            cameraChild.name = "Luminance Camera";
            cameraChild.transform.parent = cam.gameObject.transform;
            luminanceCamera = cameraChild.AddComponent(typeof(Camera)) as Camera;
            luminanceCamera.fieldOfView = cam.fieldOfView;
            luminanceCamera.targetTexture = new RenderTexture(luminanceWidth, luminanceHeight, 24);
            cameraChild.transform.localPosition = new Vector3(0, 0, 0);
            cameraChild.transform.localRotation = new Quaternion(0, 0, 0, 0);
            luminanceCreated = true;
        }

        public void PupilCalibration()
        {
            fearIncrements = new List<float>();
            StartCoroutine(PupilCalibrationCo());
        }

        private IEnumerator PupilCalibrationCo()
        {
            UnityEngine.Debug.Log("Starting Pupil Calibration");
            RawImage rawImage = calibrationScreen.GetComponent(typeof(RawImage)) as RawImage;
            UnityEngine.Debug.LogFormat("Starting pupil calibration now...");
            float luminanceDelay = 10.0f;
            int colorVal = 0;
            byte colorByte = (byte)colorVal;
            //UnityMainThreadDispatcher.Instance().Enqueue(() => PupilCalibrationLog("pupil_calibration_started", 0f));
            calibrationScreen.SetActive(true);
            for (int i = 0; i < 18; i++)
            {
                UnityEngine.Debug.Log($"Doing something{i}");
                colorVal = i * 15;
                colorByte = (byte)colorVal;
                rawImage.color = new Color32(colorByte, colorByte, colorByte, 255);
                //UnityMainThreadDispatcher.Instance().Enqueue(() => PupilCalibrationLog(string.Format("pupil_calibration_{0}", colorVal), luminanceDelay));
                yield return new WaitForSecondsRealtime(luminanceDelay);
                Increment(pupilDiameterLeft, pupilDiameterRight);
                luminanceDelay = 2.0f;
            }
            calibrationScreen.SetActive(false);
            rawImage.color = new Color32(0, 0, 0, 255);
            //UnityMainThreadDispatcher.Instance().Enqueue(() => PupilCalibrationLog("pupil_calibration_ended", 0f));
            UpdateIncrements();
        }

        private static float Analysis(int timeStamp, float pupilDiameterLeft, float pupilDiameterRight, float luminance)
        {
            int fear;
            float fear_answer = 0.0F;
            if (fearChecked && fearEnabled && fearConfigured)
            {
                if (fearStart == -1)
                {
                    fearStart = timeStamp;
                }
                fearPupil.Add(pupilDiameterLeft);
                fearLuminance.Add(luminance);
                if (timeStamp > fearStart + fearMs)
                {
                    fear_answer = CalculateFear();
                    fearStart = -1;
                    fearPupil = new List<float>();
                    fearLuminance = new List<float>();

                }
            }
            return fear_answer;
        }

        private static void Increment(float pupilDiameterLeft, float pupilDiameterRight)
        {
            // Should calculate fear instead...

            if (fearChecked && fearEnabled)
            {
                // send averageDiameter?
                fearIncrements.Add(pupilDiameterLeft);
            }

        }

        public void UpdateIncrements()
        {
            Increments inc = new Increments(fearIncrements);

            fearConfigured = true;
            UnityEngine.Debug.Log("?? Fear increments configured");

        }

        public static float CalculateFear()
        {
            Fear fear = new Fear(fearPupil, fearLuminance);
            return 0.019F;
        }
    }
}

[System.Serializable]
public class Increments
{
    public string increments;
    public Increments (List<float> values)
    {
        increments = string.Join(",", values);
    }
}

[System.Serializable]
public class Fear
{
    public string pupil;
    public string luminance;
    public Fear (List<float> pupilValues, List<float> luminanceValues)
    {
        pupil = string.Join(",", pupilValues);
        luminance = string.Join(",", luminanceValues);
    }
}
