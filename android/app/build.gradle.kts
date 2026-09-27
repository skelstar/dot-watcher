import java.util.Properties

plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.android)
    alias(libs.plugins.kotlin.compose)
    alias(libs.plugins.kotlin.serialization)
}

// Secrets that shouldn't be committed (LINZ API key) live in local.properties, the same
// gitignored, per-checkout file Android already uses for the SDK path. See README.md for
// how to obtain a key; client/.env.example follows the equivalent convention for the web
// client's VITE_LINZ_API_KEY.
val localProperties = Properties().apply {
    val file = rootProject.file("local.properties")
    if (file.exists()) {
        file.inputStream().use { load(it) }
    }
}
val linzApiKey: String = localProperties.getProperty("LINZ_API_KEY", "")
// LAN address of the dev server, for the "device" build type below — a real phone can't resolve
// 10.0.2.2 (that's the emulator's own loopback alias) or a .local mDNS hostname the way iOS's
// jakkuu.local (project.pbxproj's Device config) can, so this is a plain LAN IP instead. Falls
// back to a value that fails obviously (unresolvable host) rather than silently pointing at
// nothing, if a developer forgets to set it.
val deviceApiBaseUrl: String = localProperties.getProperty("DEVICE_API_BASE_URL", "http://192.0.2.0:8080")

android {
    namespace = "nz.skelstar.dotwatcher"
    compileSdk = 35

    defaultConfig {
        applicationId = "nz.skelstar.dotwatcher"
        minSdk = 26
        targetSdk = 35
        versionCode = 1
        versionName = "0.1.0"

        buildConfigField("String", "LINZ_API_KEY", "\"$linzApiKey\"")
    }

    // Three build types, matching iOS's Debug/Device/Release split
    // (ios/DotWatcher/DotWatcher.xcodeproj/project.pbxproj) — which target to use depends on
    // where the dev server you're testing against is reachable from:
    //   debug   - emulator, dev server on this same machine
    //   device  - real phone (USB or Wi-Fi), dev server on this machine's LAN
    //   release - any device, production server
    buildTypes {
        // Points at the Android emulator's host-loopback alias for the local dev server
        // (see docker-compose.yml / server/README.md for running it locally; launchSettings.json
        // fixes the port at 8080). Only reachable from the emulator, not a real device — see
        // "device" below for that case.
        debug {
            buildConfigField("String", "API_BASE_URL", "\"http://10.0.2.2:8080\"")
        }
        // For running on a real, physically connected phone against a dev server on your LAN.
        // Requires DEVICE_API_BASE_URL in local.properties (see local.properties.example) set to
        // this machine's LAN IP, e.g. "http://192.168.1.23:8080" — found via `ipconfig getifaddr
        // en0` on macOS. Phone and dev machine must be on the same Wi-Fi network.
        create("device") {
            initWith(getByName("debug"))
            buildConfigField("String", "API_BASE_URL", "\"$deviceApiBaseUrl\"")
        }
        release {
            isMinifyEnabled = false
            // Matches DOTWATCHER_API_BASE_URL in ios/DotWatcher/DotWatcher.xcodeproj/project.pbxproj's
            // Release configuration.
            buildConfigField("String", "API_BASE_URL", "\"https://dot-watcher.skelstar.io/api\"")
            proguardFiles(
                getDefaultProguardFile("proguard-android-optimize.txt"),
                "proguard-rules.pro"
            )
        }
    }

    buildFeatures {
        compose = true
        buildConfig = true
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    kotlinOptions {
        jvmTarget = "17"
    }
}

dependencies {
    implementation(libs.androidx.core.ktx)
    implementation(libs.androidx.lifecycle.runtime.ktx)
    implementation(libs.androidx.lifecycle.viewmodel.ktx)
    implementation(libs.androidx.lifecycle.viewmodel.compose)
    implementation(libs.androidx.activity.compose)
    implementation(platform(libs.androidx.compose.bom))
    implementation(libs.androidx.ui)
    implementation(libs.androidx.ui.graphics)
    implementation(libs.androidx.ui.tooling.preview)
    implementation(libs.androidx.material3)
    debugImplementation(libs.androidx.ui.tooling)

    implementation(libs.androidx.security.crypto)

    implementation(libs.kotlinx.serialization.json)
    implementation(libs.kotlinx.coroutines.play.services)
    implementation(libs.retrofit.core)
    implementation(libs.retrofit.kotlinx.serialization.converter)
    implementation(libs.okhttp.logging.interceptor)

    implementation(libs.play.services.location)
    implementation(libs.maplibre.android.sdk)
}
