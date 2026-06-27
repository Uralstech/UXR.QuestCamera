import org.jetbrains.kotlin.gradle.dsl.JvmTarget
import java.util.Properties

plugins {
    alias(libs.plugins.android.library)
}

val moduleNamespace = "com.uralstech.uxr.questcamera"

base {
    archivesName.set(moduleNamespace)
}

android {
    namespace = moduleNamespace
    compileSdk = 36

    defaultConfig {
        minSdk = 29
        consumerProguardFiles("consumer-rules.pro")

        ndk {
            abiFilters.clear()
            abiFilters.add("arm64-v8a")
        }
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

    externalNativeBuild {
        cmake {
            path = file("src/main/cpp/CMakeLists.txt")
            version = "3.22.1"
        }
    }

    kotlin {
        compilerOptions {
            jvmTarget.set(JvmTarget.JVM_11)
        }
    }
}

dependencies {

    implementation(libs.androidx.core.ktx)
    implementation(libs.androidx.camera.camera2)
}

tasks.register("buildVulkan") {
    val localProperties = Properties().apply {
        load(rootProject.file("local.properties").inputStream())
    }

    val sdkDir: String = localProperties.getProperty("sdk.dir")
    val ndkDir = file("$sdkDir/ndk/${android.ndkVersion}")
    val cmakeExe = file("$sdkDir/cmake/${android.externalNativeBuild.cmake.version}/bin/cmake").absolutePath

    val abi = android.defaultConfig.ndk.abiFilters.single()
    val buildDir = rootProject.file("VulkanPlugin/build/$abi")

    doLast {
        buildDir.mkdirs()

        val configProcess = ProcessBuilder(
            cmakeExe,
            "-S", rootProject.file("VulkanPlugin").absolutePath,
            "-B", buildDir.absolutePath,
            "-DCMAKE_TOOLCHAIN_FILE=${ndkDir}/build/cmake/android.toolchain.cmake",
            "-DANDROID_ABI=$abi",
            "-DANDROID_PLATFORM=android-${android.defaultConfig.minSdk}",
            "-DCMAKE_BUILD_TYPE=Release"
        )
            .redirectErrorStream(true)
            .start()

        configProcess.inputStream.bufferedReader().useLines { lines ->
            lines.forEach(logger::lifecycle)
        }

        check(configProcess.waitFor() == 0) { "CMake configure failed." }

        val buildProcess = ProcessBuilder(
            cmakeExe,
            "--build",
            buildDir.absolutePath
        )
            .redirectErrorStream(true)
            .start()

        buildProcess.inputStream.bufferedReader().useLines { lines ->
            lines.forEach(logger::lifecycle)
        }

        check(buildProcess.waitFor() == 0) { "CMake build failed." }
    }
}