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

    buildTypes {
        // Points at the Android emulator's host-loopback alias for the local dev server
        // (see docker-compose.yml / server/README.md for running it locally; launchSettings.json
        // fixes the port at 8080). Mirrors iOS's Debug/Device/Release split (README.md, "ios"
        // section) — this "debug" type stands in for iOS's Debug config until a real device / LAN
        // dev server variant is needed (Milestone 3).
        debug {
            buildConfigField("String", "API_BASE_URL", "\"http://10.0.2.2:8080\"")
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
