#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>
#import <dlfcn.h>
#import <dispatch/dispatch.h>
#import <string.h>

// App Tracking Transparency diagnostics for the Wagenheimer Tracking package.
//
// This file is compiled into every iOS build, including builds that never ask for ATT, so it
// must not create a hard link dependency on AppTrackingTransparency.framework. The framework is
// resolved with dlopen + NSClassFromString, exactly like Unity's ios-support package does.

static Class WTTrackingManagerClass(void)
{
    static Class cls = Nil;
    static bool resolved = false;

    if (!resolved)
    {
        resolved = true;
        if (@available(iOS 14.0, *))
        {
            cls = NSClassFromString(@"ATTrackingManager");
            if (cls == Nil)
            {
                void *handle = dlopen("/System/Library/Frameworks/AppTrackingTransparency.framework/AppTrackingTransparency", RTLD_LAZY);
                (void)handle;
                cls = NSClassFromString(@"ATTrackingManager");
            }
        }
    }

    return cls;
}

// History text can be long, so the buffer is generous.
static char WTBuffer[4096];

static const char *WTCopy(NSString *value)
{
    if (value == nil)
    {
        WTBuffer[0] = 0;
        return WTBuffer;
    }

    const char *utf8 = [value UTF8String];
    if (utf8 == NULL)
    {
        WTBuffer[0] = 0;
        return WTBuffer;
    }

    strncpy(WTBuffer, utf8, sizeof(WTBuffer) - 1);
    WTBuffer[sizeof(WTBuffer) - 1] = 0;
    return WTBuffer;
}

extern "C"
{
    // 1 when ATTrackingManager exists on this OS/device.
    int _WTTrackingFrameworkAvailable(void)
    {
        return WTTrackingManagerClass() != Nil ? 1 : 0;
    }

    // 1 when NSUserTrackingUsageDescription exists and is not empty in the app bundle.
    int _WTTrackingHasPurposeString(void)
    {
        NSString *value = [[NSBundle mainBundle] objectForInfoDictionaryKey:@"NSUserTrackingUsageDescription"];
        return (value != nil && value.length > 0) ? 1 : 0;
    }

    // ATTrackingManager.trackingAuthorizationStatus: 0..3, or -1 when unavailable.
    int _WTTrackingStatus(void)
    {
        Class cls = WTTrackingManagerClass();
        if (cls == Nil)
            return -1;

        // Unity compiles iOS plugins with -fno-objc-exceptions, so @try/@catch is not available.
        // Guard the KVC lookup with respondsToSelector instead.
        SEL statusSelector = NSSelectorFromString(@"trackingAuthorizationStatus");
        if (![cls respondsToSelector:statusSelector])
            return -1;

        NSNumber *status = [cls valueForKey:@"trackingAuthorizationStatus"];
        return status != nil ? (int)[status unsignedIntegerValue] : -1;
    }

    const char *_WTTrackingPurposeString(void)
    {
        return WTCopy([[NSBundle mainBundle] objectForInfoDictionaryKey:@"NSUserTrackingUsageDescription"]);
    }

    const char *_WTBundleShortVersion(void)
    {
        return WTCopy([[NSBundle mainBundle] objectForInfoDictionaryKey:@"CFBundleShortVersionString"]);
    }

    const char *_WTBundleBuildNumber(void)
    {
        return WTCopy([[NSBundle mainBundle] objectForInfoDictionaryKey:@"CFBundleVersion"]);
    }

    // Opens this app's page in Settings (iOS 8+), where the tracking permission can be changed.
    // Returns 1 when the request was handed to the system, 0 otherwise. Dispatched to the main
    // queue because the overlay may call it from a UI callback.
    int _WTTrackingOpenSettings(void)
    {
        NSURL *url = [NSURL URLWithString:UIApplicationOpenSettingsURLString];
        if (url == nil)
            return 0;

        dispatch_async(dispatch_get_main_queue(), ^{
            [[UIApplication sharedApplication] openURL:url options:@{} completionHandler:nil];
        });

        return 1;
    }

    // Used by the runtime overlay ("Copy ..."). GUIUtility.systemCopyBuffer is not reliable on
    // every iOS version, so the managed side prefers this native path.
    void _WTCopyToClipboard(const char *text)
    {
        if (text == NULL)
            return;

        NSString *value = [NSString stringWithUTF8String:text];
        if (value == nil)
            return;

        [[UIPasteboard generalPasteboard] setString:value];
    }
}
