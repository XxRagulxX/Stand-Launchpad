cmake_minimum_required(VERSION 3.20)
project(Launchpad NONE)

# ---------------------------------------------------------------------------
# CMake orchestrates the build; the .NET SDK still compiles the C# code.
# There is no C# compiler support in CMake itself, so every target below is a
# custom command that shells out to `dotnet`. This gives one command
# (`cmake --build build`) that works identically on Windows and Linux,
# replacing the old .sln.
# ---------------------------------------------------------------------------

set(LAUNCHPAD_CONFIGURATION "Release" CACHE STRING "Build configuration (Debug/Release)")

if(WIN32)
    set(LAUNCHPAD_UI_RID "win-x64" CACHE STRING "RID for the Launchpad UI publish")
else()
    set(LAUNCHPAD_UI_RID "linux-x64" CACHE STRING "RID for the Launchpad UI publish")
endif()

# The injector is a Windows/Wine-only binary regardless of host OS: on
# Linux it still gets built for win-x64 and is invoked through `wine`.
set(LAUNCHPAD_INJECTOR_RID "win-x64")

set(LAUNCHPAD_OUT "${CMAKE_BINARY_DIR}/publish")

# Deliberately publish to the default bin/<config>/<tfm>/<rid>/publish
# location (no -o override): Launchpad.csproj's <None Include> glob for the
# Injector's files points at that exact path, so `dotnet publish` on the
# Launchpad project can pick up the already-published Injector output.
add_custom_target(injector ALL
    COMMAND dotnet publish "${CMAKE_SOURCE_DIR}/Injector/Injector.csproj"
            -c ${LAUNCHPAD_CONFIGURATION}
            -r ${LAUNCHPAD_INJECTOR_RID}
    WORKING_DIRECTORY ${CMAKE_SOURCE_DIR}
    COMMENT "Publishing Launchpad.Injector (win-x64, runs directly on Windows or via wine on Linux)"
    VERBATIM
)

# --- Launchpad UI ------------------------------------------------------
add_custom_target(launchpad ALL
    COMMAND dotnet publish "${CMAKE_SOURCE_DIR}/Launchpad/Launchpad.csproj"
            -c ${LAUNCHPAD_CONFIGURATION}
            -r ${LAUNCHPAD_UI_RID}
            -o "${LAUNCHPAD_OUT}"
    WORKING_DIRECTORY ${CMAKE_SOURCE_DIR}
    COMMENT "Publishing Launchpad UI (${LAUNCHPAD_UI_RID})"
    VERBATIM
)
add_dependencies(launchpad injector)

# --- Tests ------------------------------------------------------------
if(EXISTS "${CMAKE_SOURCE_DIR}/Launchpad.Tests/Launchpad.Tests.csproj")
    add_custom_target(test_launchpad
        COMMAND dotnet test "${CMAKE_SOURCE_DIR}/Launchpad.Tests/Launchpad.Tests.csproj" -c ${LAUNCHPAD_CONFIGURATION}
        WORKING_DIRECTORY ${CMAKE_SOURCE_DIR}
        COMMENT "Running Launchpad.Tests"
        VERBATIM
    )
endif()
