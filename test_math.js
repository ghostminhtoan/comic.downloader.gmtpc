var n = "123";
try {
  WScript.Echo("n[0] is: " + n[0]);
} catch(e) {
  WScript.Echo("Error: " + e.message);
}
