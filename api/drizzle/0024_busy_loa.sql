CREATE TABLE `player_stats` (
	`id` integer PRIMARY KEY AUTOINCREMENT NOT NULL,
	`modpack_id` integer NOT NULL,
	`name` text NOT NULL,
	`biome` text,
	`armor` integer,
	`last_seen_at` integer NOT NULL,
	FOREIGN KEY (`modpack_id`) REFERENCES `modpacks`(`id`) ON UPDATE no action ON DELETE no action
);
--> statement-breakpoint
CREATE UNIQUE INDEX `player_stats_modpack_name_idx` ON `player_stats` (`modpack_id`,`name`);