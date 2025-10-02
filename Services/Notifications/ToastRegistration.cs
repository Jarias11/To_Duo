using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;                                // StringBuilder
using Microsoft.Toolkit.Uwp.Notifications;        // DesktopNotificationManagerCompat, ToastContentBuilder
using Windows.UI.Notifications;

namespace TaskMate.Services.Notifications {
	public static class ToastRegistration {
		public static void EnsureRegistered() {
			// Required on Win10: Start menu shortcut that binds the AUMID to your EXE
			EnsureShortcut(ToastIds.Aumid);

			// Register AUMID + COM activator (generic requires our ToastActivator type)
			DesktopNotificationManagerCompat.RegisterAumidAndComServer<ToastActivator>(ToastIds.Aumid);
			DesktopNotificationManagerCompat.RegisterActivator<ToastActivator>();
		}

		public static void ShowSimple(string title, string body, string arguments = "") {
			var xml = new ToastContentBuilder()
				.AddText(title)
				.AddText(body)
				.AddArgument("args", arguments)
				.GetXml();

			var toast = new ToastNotification(xml);

			// After registration, use the compat notifier (no AUMID param)
			DesktopNotificationManagerCompat.CreateToastNotifier().Show(toast);
		}

		// ---------------- Start menu shortcut helpers ----------------

		private static void EnsureShortcut(string aumid) {
			string startMenu = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
			string shortcutDir = Path.Combine(startMenu, "Programs");
			Directory.CreateDirectory(shortcutDir);

			string shortcutPath = Path.Combine(shortcutDir, "TaskMate.lnk");
			if(File.Exists(shortcutPath)) return; // good enough for most cases

			CreateShortcut(shortcutPath, aumid);
		}

		[ComImport, Guid("00021401-0000-0000-C000-000000000046")]
		private class ShellLink { }

		[ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
		private interface IShellLinkW {
			void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, int fFlags);
			void GetIDList(out IntPtr ppidl);
			void SetIDList(IntPtr pidl);
			void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
			void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
			void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
			void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
			void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
			void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
			void GetHotkey(out short pwHotkey);
			void SetHotkey(short wHotkey);
			void GetShowCmd(out int piShowCmd);
			void SetShowCmd(int iShowCmd);
			void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
			void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
			void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
			void Resolve(IntPtr hwnd, int fFlags);
			void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
		}

		[ComImport, Guid("0000010b-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
		private interface IPersistFile {
			void GetClassID(out Guid pClassID);
			void IsDirty();
			void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
			void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, bool fRemember);
			void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
			void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
		}

		[ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
		private interface IPropertyStore {
			void GetCount(out uint cProps);
			void GetAt(uint iProp, out PROPERTYKEY pkey);
			void GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
			void SetValue(ref PROPERTYKEY key, ref PROPVARIANT pv);
			void Commit();
		}

		[StructLayout(LayoutKind.Sequential, Pack = 4)]
		private struct PROPERTYKEY { public Guid fmtid; public uint pid; }

		[StructLayout(LayoutKind.Explicit)]
		private struct PROPVARIANT {
			[FieldOffset(0)] public ushort vt;
			[FieldOffset(8)] public IntPtr pointerValue;
		}

		private static readonly PROPERTYKEY PKEY_AppUserModel_ID =
			new PROPERTYKEY { fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), pid = 5 };
		private static readonly PROPERTYKEY PKEY_AppUserModel_ToastActivatorCLSID =
			new PROPERTYKEY { fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), pid = 26 };

		private static void CreateShortcut(string shortcutPath, string aumid) {
			var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName!;
			var link = (IShellLinkW)new ShellLink();
			link.SetPath(exePath);
			link.SetDescription("TaskMate");

			var file = (IPersistFile)link;
			file.Save(shortcutPath, false);

			var propStore = (IPropertyStore)link;
			SetString(propStore, PKEY_AppUserModel_ID, aumid);
			SetString(propStore, PKEY_AppUserModel_ToastActivatorCLSID, "{" + ToastIds.ActivatorClsid + "}");
			propStore.Commit();
			file.Save(shortcutPath, false);
		}

		private static void SetString(IPropertyStore store, PROPERTYKEY key, string value) {
			var pv = new PROPVARIANT { vt = 31, pointerValue = Marshal.StringToCoTaskMemUni(value) }; // VT_LPWSTR
			store.SetValue(ref key, ref pv);
			Marshal.FreeCoTaskMem(pv.pointerValue);
		}
	}

}