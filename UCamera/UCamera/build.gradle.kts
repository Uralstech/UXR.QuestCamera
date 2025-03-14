plugins {
    alias(libs.plugins.android.library)
    alias(libs.plugins.kotlin.android)
}

android {
    namespace = "com.uralstech.ucamera"
    compileSdk = 35

    defaultConfig {
        minSdk = 28

        setProperty("archivesBaseName", "$namespace")
    }

    buildTypes {
        release {
            isMinifyEnabled = false
            proguardFiles(
                getDefaultProguardFile("proguard-android-optimize.txt"),
                "proguard-rules.pro"
            )
        }
    }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_11
        targetCompatibility = JavaVersion.VERSION_11
    }
    kotlinOptions {
        jvmTarget = "11"
    }

    packaging {
        resources {
            excludes.add("com/unity3d/**")
        }
    }
}

dependencies {
    compileOnly(files("libs/classes.jar"))
    implementation(libs.androidx.core.ktx)
    implementation(libs.androidx.camera.camera2)
}