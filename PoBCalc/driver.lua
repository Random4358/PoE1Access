-- Runs Path of Building without its window and writes its main numbers to POBCALC_OUT as
-- "key=value" lines. After Path of Building's own HeadlessWrapper.lua (MIT, Copyright (c) 2016
-- David Gowor; see LICENSE-PathOfBuilding.txt).
--
-- POBCALC_MODE "build": a build from a build code, as saved.
-- POBCALC_MODE "character": a character described by PoE1Access from the game. Its passives are
--   given with no ascendancy (the game does not say which); the ascendancy is found from the
--   ascendancy passives taken, and the build loaded again with it.
-- POBCALC_MODE "compare": POBCALC_XML is a list of character files, one path a line (the first
--   the character as it is, the others with one thing changed); each is calculated in turn and
--   its numbers written with "v1.", "v2."... before each key. Path of Building starts once.

local out = assert(io.open(POBCALC_OUT, "w"))
local function put(k, v) out:write(k, "=", (tostring(v):gsub("\n", " ")), "\n") out:flush() end

-- A headless run must not take Path of Building's first-run update.
local l_open = io.open
io.open = function(name, mode) if name == "first.run" then return nil end return l_open(name, mode) end

arg = arg or { }
dofile(POBCALC_DIR .. "/_SimpleGraphic.def.lua")
function GetVirtualScreenSize() return 1920, 1080 end
__callbackTable__ = { }
function runCallback(name, ...)
	if __callbackTable__[name] then return __callbackTable__[name](...)
	elseif __mainObject__ and __mainObject__[name] then return __mainObject__[name](__mainObject__, ...) end
end
local l_require = require
function require(name)
	if name == "lcurl.safe" then return end
	return l_require(name)
end
package.path = "./lua/?.lua;./lua/?/init.lua;" .. package.path
package.cpath = "./?.dll;" .. package.cpath

dofile("Launch.lua")
__mainObject__.continuousIntegrationMode = os.getenv("CI")
runCallback("OnInit")
runCallback("OnFrame")
if __mainObject__.promptMsg then put("error", __mainObject__.promptMsg) out:close() return end

local function readFile(path)
	local f = assert(l_open(path, "r"))
	local t = f:read("*a") f:close()
	return t
end

local function load(text)
	__mainObject__.main:SetMode("BUILD", false, "", text)
	runCallback("OnFrame")
	return __mainObject__.main.modes["BUILD"]
end

local keys = { "FullDPS", "CombinedDPS", "TotalDPS", "TotalDot", "Speed", "HitChance", "CritChance",
	"Life", "LifeUnreserved", "EnergyShield", "Mana", "ManaUnreserved", "Armour", "Evasion",
	"FireResist", "ColdResist", "LightningResist", "ChaosResist", "BlockChance", "SpellBlockChance",
	"TotalEHP", "PhysicalMaximumHitTaken", "FireMaximumHitTaken", "ColdMaximumHitTaken",
	"LightningMaximumHitTaken", "ChaosMaximumHitTaken", "LifeRegenRecovery", "MovementSpeedMod",
	"Str", "Dex", "Int", "ReqStr", "ReqDex", "ReqInt" }

-- One build: loaded (again with its ascendancy for a character), its numbers written.
local function calc(xmlText, character, prefix)
	if latestTreeVersion then xmlText = xmlText:gsub('treeVersion="LATEST"', 'treeVersion="' .. latestTreeVersion .. '"') end
	local build = load(xmlText)
	if character and build.spec and build.spec.tree then
		local nodes = xmlText:match('nodes="([^"]*)"') or ""
		local ascName
		for id in nodes:gmatch("%d+") do
			local n = build.spec.tree.nodes[tonumber(id)]
			if n and n.ascendancyName then ascName = n.ascendancyName break end
		end
		if ascName then
			for cid, cls in pairs(build.spec.tree.classes or { }) do
				for aid, asc in pairs(cls.classes or { }) do
					if asc.name == ascName then
						local text = xmlText:gsub('classId="%d+"', 'classId="' .. tostring(cid) .. '"')
						                    :gsub('ascendClassId="%d+"', 'ascendClassId="' .. tostring(aid) .. '"')
						build = load(text)
						put(prefix .. "Ascendancy", ascName)
					end
				end
			end
		end
	end
	local o = build.calcsTab and build.calcsTab.mainOutput
	if not o then put(prefix .. "error", "no calculations") return end
	put(prefix .. "ok", 1)
	for _, k in ipairs(keys) do
		if o[k] ~= nil then put(prefix .. k, o[k]) end
	end
	local grp = build.skillsTab and build.skillsTab.socketGroupList and build.skillsTab.socketGroupList[build.mainSocketGroup]
	if grp and grp.displaySkillList and grp.displaySkillList[grp.mainActiveSkill or 1] then
		local s = grp.displaySkillList[grp.mainActiveSkill or 1]
		if s.activeEffect and s.activeEffect.grantedEffect then put(prefix .. "MainSkill", s.activeEffect.grantedEffect.name) end
	end
end

if POBCALC_MODE == "compare" then
	local i = 0
	for path in readFile(POBCALC_XML):gmatch("[^\r\n]+") do
		i = i + 1
		local ok, err = pcall(calc, readFile(path), true, "v" .. i .. ".")
		if not ok then put("v" .. i .. ".error", err) end
	end
	put("ok", 1)
else
	calc(readFile(POBCALC_XML), POBCALC_MODE == "character", "")
end
out:close()
